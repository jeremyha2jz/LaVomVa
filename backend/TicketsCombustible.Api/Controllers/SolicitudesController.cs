using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TicketsCombustible.Api.Contracts;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;
using TicketsCombustible.Api.Services;

namespace TicketsCombustible.Api.Controllers;

[ApiController]
[Route("api/solicitudes")]
public class SolicitudesController(TicketsCombustibleDbContext db, IAuditoriaService auditoria) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> Listar() => Ok(await db.Solicitudes.OrderByDescending(x => x.Id).ToListAsync());
    [HttpGet("{id:long}")] public async Task<IActionResult> Obtener(long id) => await db.Solicitudes.FindAsync(id) is { } solicitud ? Ok(solicitud) : NotFound();

    [HttpPost]
    [Authorize(Roles = "ADMINISTRADOR,SUPERVISOR,SOLICITANTE")]
    public async Task<IActionResult> Crear(CrearSolicitudRequest request)
    {
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Unauthorized();
        if (request.CantidadSolicitadaGalones <= 0) return BadRequest("La cantidad solicitada debe ser mayor que cero.");
        DateTime? fechaVencimiento = request.FechaVencimiento is { } vence ? FechaSinZona(vence) : null;
        if (fechaVencimiento <= DateTime.UtcNow) return BadRequest("La fecha de vencimiento debe ser futura.");
        if (!await db.Empleados.AnyAsync(x => x.Id == request.EmpleadoId && x.Activo) || !await db.Vehiculos.AnyAsync(x => x.Id == request.VehiculoId && x.Activo) || !await db.Departamentos.AnyAsync(x => x.Id == request.DepartamentoId && x.Activo) || !await db.TiposCombustible.AnyAsync(x => x.Id == request.TipoCombustibleId && x.Activo)) return BadRequest("Selecciona un empleado, vehículo, departamento y combustible válidos.");
        var solicitud = new SolicitudCombustible { EmpleadoId = request.EmpleadoId, VehiculoId = request.VehiculoId, DepartamentoId = request.DepartamentoId, TipoCombustibleId = request.TipoCombustibleId, CantidadSolicitadaGalones = request.CantidadSolicitadaGalones, FechaVencimiento = fechaVencimiento, UsuarioCreadorId = actorId, TipoSolicitud = request.TipoSolicitud, Motivo = request.Motivo, Estado = EstadoSolicitud.PENDIENTE };
        db.Solicitudes.Add(solicitud);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        await auditoria.RegistrarAsync("REQUEST_CREATED", "SOLICITUD", solicitud.Id.ToString(), "EXITO", datosNuevos: Snapshot(solicitud));
        await transaction.CommitAsync();
        return CreatedAtAction(nameof(Obtener), new { id = solicitud.Id }, solicitud);
    }

    [HttpPut("{id:long}/aprobar")]
    [Authorize(Roles = "ADMINISTRADOR,SUPERVISOR")]
    public async Task<IActionResult> Aprobar(long id, AprobarSolicitudRequest request)
    {
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Unauthorized();
        var solicitud = await db.Solicitudes.FindAsync(id);
        if (solicitud is null) return NotFound();
        if (solicitud.Estado != EstadoSolicitud.PENDIENTE) return Conflict("Solo se puede aprobar una solicitud pendiente.");
        var before = Snapshot(solicitud);
        if (request.CantidadAutorizadaGalones <= 0 || request.CantidadAutorizadaGalones > solicitud.CantidadSolicitadaGalones) return BadRequest("La cantidad autorizada debe ser válida.");
        var fechaVencimiento = FechaSinZona(request.FechaVencimiento);
        if (fechaVencimiento <= DateTime.UtcNow) return BadRequest("La fecha de vencimiento debe ser futura.");
        solicitud.CantidadAutorizadaGalones = request.CantidadAutorizadaGalones; solicitud.FechaVencimiento = fechaVencimiento; solicitud.UsuarioAprobadorId = actorId; solicitud.FechaAprobacion = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified); solicitud.Estado = EstadoSolicitud.APROBADA;
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        await auditoria.RegistrarAsync("REQUEST_APPROVED", "SOLICITUD", solicitud.Id.ToString(), "EXITO", before, Snapshot(solicitud));
        await transaction.CommitAsync();
        return Ok(solicitud);
    }

    [HttpPut("{id:long}/rechazar")]
    [Authorize(Roles = "ADMINISTRADOR,SUPERVISOR")]
    public async Task<IActionResult> Rechazar(long id)
    {
        var solicitud = await db.Solicitudes.FindAsync(id);
        if (solicitud is null) return NotFound();
        if (solicitud.Estado != EstadoSolicitud.PENDIENTE) return Conflict("Solo se puede rechazar una solicitud pendiente.");
        var before = Snapshot(solicitud);
        solicitud.Estado = EstadoSolicitud.RECHAZADA;
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        await auditoria.RegistrarAsync("REQUEST_REJECTED", "SOLICITUD", solicitud.Id.ToString(), "EXITO", before, Snapshot(solicitud));
        await transaction.CommitAsync();
        return Ok(solicitud);
    }

    private static object Snapshot(SolicitudCombustible x) => new
    {
        x.Id, x.EmpleadoId, x.VehiculoId, x.DepartamentoId, x.TipoCombustibleId,
        x.CantidadSolicitadaGalones, x.CantidadAutorizadaGalones, x.FechaSolicitud,
        x.FechaVencimiento, x.TipoSolicitud, x.Estado, x.UsuarioCreadorId,
        x.UsuarioAprobadorId, x.FechaAprobacion
    };

    // Las columnas son "timestamp without time zone" y guardan la hora en UTC: una fecha con zona (ej. "...Z" o "-04:00") se pasa a UTC antes de quitarle la zona, para no guardar la hora local como si fuera UTC.
    private static DateTime FechaSinZona(DateTime fecha) => DateTime.SpecifyKind(fecha.Kind == DateTimeKind.Unspecified ? fecha : fecha.ToUniversalTime(), DateTimeKind.Unspecified);
}
