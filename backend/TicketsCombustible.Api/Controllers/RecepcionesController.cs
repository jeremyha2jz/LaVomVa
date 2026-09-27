using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;
using Npgsql;
using TicketsCombustible.Api.Services;

namespace TicketsCombustible.Api.Controllers;

[ApiController]
[Route("api/recepciones")]
public class RecepcionesController(TicketsCombustibleDbContext db, IAuditoriaService auditoria, NotificacionService notifications, IInventoryRealtimePublisher realtime) : ControllerBase
{
    [HttpGet("proveedores")]
    public async Task<IActionResult> Proveedores() => Ok(await db.Proveedores.Where(x => x.Activo).OrderBy(x => x.Nombre).ToListAsync());

    [HttpPost("proveedores")]
    [Authorize(Roles = "ADMINISTRADOR,SUPERVISOR")]
    public async Task<IActionResult> CrearProveedor(Proveedor proveedor)
    {
        if (string.IsNullOrWhiteSpace(proveedor.Nombre)) return BadRequest("El nombre del proveedor es obligatorio.");
        db.Proveedores.Add(proveedor);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        await auditoria.RegistrarAsync("SUPPLIER_CREATED", "PROVEEDOR", proveedor.Id.ToString(), "EXITO", datosNuevos: new { proveedor.Id, proveedor.Nombre, proveedor.Rnc, proveedor.Telefono, proveedor.Correo, proveedor.Activo });
        await transaction.CommitAsync();
        return Created($"api/recepciones/proveedores/{proveedor.Id}", proveedor);
    }

    [HttpPost]
    [Authorize(Roles = "ADMINISTRADOR,SUPERVISOR")]
    public async Task<IActionResult> Crear(CrearRecepcionRequest request)
    {
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Unauthorized();
        if (request.Detalles.Count == 0) return BadRequest("Debe incluir al menos un tanque.");
        if (!await db.Proveedores.AnyAsync(x => x.Id == request.ProveedorId && x.Activo)) return BadRequest("Proveedor inválido.");
        if (!await db.Usuarios.AnyAsync(x => x.Id == actorId && x.Activo)) return BadRequest("Usuario receptor inválido.");
        if (request.Detalles.Any(x => x.VolumenRecibidoGalones <= 0)) return BadRequest("Cada volumen debe ser mayor que cero.");
        if (request.Detalles.GroupBy(x => x.TanqueId).Any(x => x.Count() > 1)) return BadRequest("No repita un tanque en la misma recepción.");
        if (await db.Tanques.CountAsync(x => request.Detalles.Select(d => d.TanqueId).Contains(x.Id) && x.Activo) != request.Detalles.Count) return BadRequest("Uno o más tanques son inválidos.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var tankId in request.Detalles.Select(x => x.TanqueId).Order())
            _ = await db.Tanques.FromSqlInterpolated($"SELECT * FROM tanques WHERE id_tanque={tankId} FOR UPDATE").AsNoTracking().SingleAsync();
        var recepcion = new RecepcionCombustible { ProveedorId = request.ProveedorId, NumeroFactura = request.NumeroFactura, FechaRecepcion = DateTime.SpecifyKind(request.FechaRecepcion, DateTimeKind.Unspecified), UsuarioReceptorId = actorId, Observaciones = request.Observaciones };
        List<long> movementIds = [];
        try
        {
            db.Recepciones.Add(recepcion); await db.SaveChangesAsync();
            var detalles = request.Detalles.Select(x => new DetalleRecepcion { RecepcionId = recepcion.Id, TanqueId = x.TanqueId, VolumenRecibidoGalones = x.VolumenRecibidoGalones, CostoUnitario = x.CostoUnitario }).ToList();
            db.DetallesRecepcion.AddRange(detalles);
            await db.SaveChangesAsync();
            foreach (var tankId in request.Detalles.Select(x => x.TanqueId))
            {
                var stockAfter = await db.Tanques.AsNoTracking().Where(x => x.Id == tankId).Select(x => x.ExistenciaActualGalones).SingleAsync();
                await notifications.SincronizarEpisodioInventarioAsync(tankId, stockAfter);
            }
            var detailIds = detalles.Select(x => x.Id.ToString()).ToList();
            movementIds = await db.MovimientosInventario.AsNoTracking()
                .Where(x => x.ReferenciaTipo == "RECEPCION" && x.ReferenciaId != null && detailIds.Contains(x.ReferenciaId))
                .OrderBy(x => x.Id).Select(x => x.Id).ToListAsync();
            await auditoria.RegistrarAsync("RECEIPT_CREATED", "RECEPCION", recepcion.Id.ToString(), "EXITO",
                datosNuevos: new { recepcion.Id, recepcion.ProveedorId, recepcion.NumeroFactura, recepcion.FechaRecepcion, detalles = request.Detalles.Select(x => new { x.TanqueId, x.VolumenRecibidoGalones, x.CostoUnitario }) });
            await transaction.CommitAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.RaiseException &&
            (pg.MessageText.StartsWith("Inventario insuficiente", StringComparison.Ordinal) || pg.MessageText.StartsWith("El movimiento excede la capacidad", StringComparison.Ordinal) || pg.MessageText.StartsWith("El día operacional ya está cerrado", StringComparison.Ordinal)))
        {
            return Conflict(pg.MessageText);
        }
        foreach (var movementId in movementIds) await realtime.PublishMovementAsync(movementId);
        return Created($"api/recepciones/{recepcion.Id}", new { recepcion, request.Detalles });
    }
}

public record DetalleRecepcionRequest(long TanqueId, decimal VolumenRecibidoGalones, decimal? CostoUnitario);
public record CrearRecepcionRequest(long ProveedorId, string NumeroFactura, DateTime FechaRecepcion, long UsuarioReceptorId, string? Observaciones, List<DetalleRecepcionRequest> Detalles);
