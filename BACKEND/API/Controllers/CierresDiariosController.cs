using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TicketsCombustible.Api.Contracts;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;
using TicketsCombustible.Api.Services;

namespace TicketsCombustible.Api.Controllers;

[ApiController]
[Route("api/cierres-diarios")]
[Authorize(Roles = "ADMINISTRADOR,SUPERVISOR,DESPACHADOR,AUDITOR")]
public sealed class CierresDiariosController(
    TicketsCombustibleDbContext db,
    CierreDiarioService cierres,
    IAuditoriaService auditoria,
    TimeProvider timeProvider,
    ILogger<CierresDiariosController> logger) : ControllerBase
{
    [HttpGet("resumen")]
    public async Task<IActionResult> Resumen([FromQuery] long estacionId, [FromQuery] DateOnly fecha, CancellationToken cancellationToken)
    {
        if (estacionId <= 0) return BadRequest("Debe indicar una estación válida.");
        if (fecha > HoyUtc()) return BadRequest("No se puede cerrar un día operacional futuro.");
        try
        {
            var resumen = await cierres.CalcularAsync(estacionId, fecha, cancellationToken);
            if (resumen is null) return NotFound("Estación no encontrada.");
            var cierre = await db.CierresDiarios.AsNoTracking().SingleOrDefaultAsync(x => x.EstacionId == estacionId && x.Fecha == fecha, cancellationToken);
            return Ok(new { resumen, cerrado = cierre is not null, cierre });
        }
        catch (CierreIntegridadException ex) { return Conflict(ex.Message); }
    }

    [HttpPost]
    [Authorize(Roles = "ADMINISTRADOR,SUPERVISOR,DESPACHADOR")]
    public async Task<IActionResult> Crear(CrearCierreDiarioRequest request, CancellationToken cancellationToken)
    {
        if (request.EstacionId <= 0) return BadRequest("Debe indicar una estación válida.");
        if (request.Fecha > HoyUtc()) return BadRequest("No se puede cerrar un día operacional futuro.");
        if (request.Observaciones?.Length > 2000) return BadRequest("Las observaciones no pueden superar 2000 caracteres.");
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Unauthorized();
        if (request.InventariosFisicos is null) return BadRequest("Debe incluir los inventarios físicos por tanque.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Same station-row lock is acquired by inventory movement triggers. Whoever gets it first
            // is included in the snapshot; later movements are rejected by the closed-day trigger.
            var station = await db.Estaciones.FromSqlInterpolated($"SELECT * FROM estaciones WHERE id_estacion = {request.EstacionId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (station is null || !station.Activo) return NotFound("Estación no encontrada o inactiva.");
            if (await db.CierresDiarios.AnyAsync(x => x.EstacionId == request.EstacionId && x.Fecha == request.Fecha, cancellationToken))
                return Conflict("Ya existe un cierre para esa estación y fecha.");

            var resumen = await cierres.CalcularAsync(request.EstacionId, request.Fecha, cancellationToken);
            if (resumen is null) return NotFound("Estación no encontrada.");
            var fisicos = request.InventariosFisicos;
            if (fisicos.GroupBy(x => x.TanqueId).Any(x => x.Count() > 1)) return BadRequest("No repita tanques en el inventario físico.");
            if (fisicos.Count != resumen.Tanques.Count || fisicos.Any(x => x.InventarioFisicoGalones is null))
                return BadRequest("Debe informar el inventario físico de cada tanque de la estación.");
            if (!resumen.Tanques.Select(x => x.TanqueId).Order().SequenceEqual(fisicos.Select(x => x.TanqueId).Order()))
                return BadRequest("Los tanques físicos deben pertenecer a la estación seleccionada.");

            var physicalByTank = fisicos.ToDictionary(x => x.TanqueId, x => x.InventarioFisicoGalones!.Value);
            foreach (var tank in resumen.Tanques)
            {
                var physical = physicalByTank[tank.TanqueId];
                if (physical < 0 || physical > tank.CapacidadGalones || decimal.Round(physical, 2) != physical)
                    return BadRequest($"El inventario físico de {tank.Codigo} debe estar entre 0 y su capacidad ({tank.CapacidadGalones:0.00}) con precisión de hasta 2 decimales.");
            }

            var physicalTotal = physicalByTank.Values.Sum();
            var difference = physicalTotal - resumen.InventarioTeoricoFinalGalones;
            var detalle = resumen.Tanques.Select(tank => new
            {
                tank.TanqueId,
                codigo = tank.Codigo,
                nombre = tank.Nombre,
                tank.CapacidadGalones,
                tank.InventarioInicialGalones,
                tank.EntradasGalones,
                tank.DespachadoGalones,
                tank.OtrasSalidasGalones,
                tank.MermasGalones,
                tank.AjustesGalones,
                tank.InventarioTeoricoFinalGalones,
                inventarioFisicoGalones = physicalByTank[tank.TanqueId],
                diferenciaGalones = physicalByTank[tank.TanqueId] - tank.InventarioTeoricoFinalGalones
            }).ToArray();

            var id = await db.Database.SqlQueryRaw<long>("SELECT nextval(pg_get_serial_sequence('cierres_diarios','id_cierre')) AS \"Value\"")
                .SingleAsync(cancellationToken);
            var cerradoEn = DateTime.SpecifyKind(timeProvider.GetUtcNow().UtcDateTime, DateTimeKind.Unspecified);
            var cierre = new CierreDiario
            {
                Id = id,
                EstacionId = request.EstacionId,
                Fecha = request.Fecha,
                InventarioInicialGalones = resumen.InventarioInicialGalones,
                VolumenRecibidoGalones = resumen.VolumenRecibidoGalones,
                VolumenDespachadoGalones = resumen.VolumenDespachadoGalones,
                MermasGalones = resumen.MermasGalones,
                AjustesGalones = resumen.AjustesGalones,
                InventarioFinalGalones = resumen.InventarioTeoricoFinalGalones,
                InventarioFisicoGalones = physicalTotal,
                DiferenciaGalones = difference,
                CantidadDespachos = resumen.CantidadDespachos,
                UsuarioCierreId = actorId,
                RutaActaPdf = $"/api/cierres-diarios/{id}/pdf",
                Observaciones = request.Observaciones?.Trim(),
                CerradoEn = cerradoEn,
                Estado = "CERRADO",
                DetalleTanques = JsonDocument.Parse(JsonSerializer.Serialize(detalle))
            };
            db.CierresDiarios.Add(cierre);
            await db.SaveChangesAsync(cancellationToken);
            await auditoria.RegistrarAsync("CIERRE_DIARIO_CREADO", "CIERRE_DIARIO", id.ToString(), "EXITO",
                datosNuevos: new
                {
                    cierre.Id, fechaOperacional = cierre.Fecha, cierre.EstacionId, cierre.InventarioInicialGalones,
                    cierre.VolumenRecibidoGalones, cierre.VolumenDespachadoGalones, cierre.CantidadDespachos,
                    cierre.InventarioFinalGalones, cierre.InventarioFisicoGalones, cierre.DiferenciaGalones, cierre.Estado
                }, cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return CreatedAtAction(nameof(Obtener), new { id }, cierre);
        }
        catch (CierreIntegridadException ex) { return Conflict(ex.Message); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
            pg.SqlState == PostgresErrorCodes.UniqueViolation && pg.ConstraintName == "uq_cierre_estacion_fecha")
        {
            return Conflict("Ya existe un cierre para esa estación y fecha.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.RaiseException)
        {
            logger.LogError(ex, "No se pudo guardar íntegramente el cierre diario.");
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "No se pudo completar el cierre diario.");
        }
    }

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta,
        [FromQuery] long? estacionId, [FromQuery] long? usuarioId, CancellationToken cancellationToken)
    {
        if (desde.HasValue && hasta.HasValue && desde > hasta) return BadRequest("La fecha inicial no puede ser posterior a la final.");
        var query = db.CierresDiarios.AsNoTracking().AsQueryable();
        if (desde.HasValue) query = query.Where(x => x.Fecha >= desde.Value);
        if (hasta.HasValue) query = query.Where(x => x.Fecha <= hasta.Value);
        if (estacionId.HasValue) query = query.Where(x => x.EstacionId == estacionId.Value);
        if (usuarioId.HasValue) query = query.Where(x => x.UsuarioCierreId == usuarioId.Value);
        var closures = await query.OrderByDescending(x => x.Fecha).ThenByDescending(x => x.Id).Take(250).ToListAsync(cancellationToken);
        var stationNames = await db.Estaciones.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Nombre, cancellationToken);
        return Ok(closures.Select(x => new { cierre = x, estacion = stationNames.GetValueOrDefault(x.EstacionId) }));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Obtener(long id, CancellationToken cancellationToken)
    {
        var cierre = await db.CierresDiarios.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return cierre is null ? NotFound() : Ok(cierre);
    }

    [HttpGet("{id:long}/pdf")]
    public async Task<IActionResult> Pdf(long id, CancellationToken cancellationToken)
    {
        var cierre = await db.CierresDiarios.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (cierre is null) return NotFound("Cierre no encontrado.");
        var estacion = await db.Estaciones.AsNoTracking().Where(x => x.Id == cierre.EstacionId).Select(x => x.Nombre).SingleAsync(cancellationToken);
        var responsable = await db.Usuarios.AsNoTracking().Where(x => x.Id == cierre.UsuarioCierreId).Select(x => x.NombreCompleto).SingleAsync(cancellationToken);
        var pdf = PdfActaCierreGenerator.Generar(cierre, estacion, responsable);
        return File(pdf, "application/pdf", $"acta-cierre-{id}.pdf");
    }

    private DateOnly HoyUtc() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
}
