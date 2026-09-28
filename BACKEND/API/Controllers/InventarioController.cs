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
[Route("api/inventario")]
public class InventarioController(TicketsCombustibleDbContext db, IAuditoriaService auditoria, NotificacionService notifications, IInventoryRealtimePublisher realtime) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Consultar() => Ok(await db.Tanques.Where(x => x.Activo).OrderBy(x => x.Codigo).Select(x => new { x.Id, x.Codigo, x.EstacionId, x.TipoCombustibleId, x.CapacidadGalones, x.ExistenciaActualGalones, disponibleGalones = x.ExistenciaActualGalones, espacioDisponibleGalones = x.CapacidadGalones - x.ExistenciaActualGalones }).ToListAsync());

    [HttpGet("movimientos")]
    public async Task<IActionResult> Movimientos([FromQuery] long? tanqueId)
    {
        var consulta = db.MovimientosInventario.AsNoTracking();
        if (tanqueId.HasValue) consulta = consulta.Where(x => x.TanqueId == tanqueId.Value);
        return Ok(await consulta.OrderByDescending(x => x.Id).Take(100).ToListAsync());
    }

    [HttpPost("ajustes")]
    [Authorize(Roles = "ADMINISTRADOR,SUPERVISOR")]
    public async Task<IActionResult> Ajustar(AjusteInventarioRequest request)
    {
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Unauthorized();
        if (request.CantidadGalones <= 0) return BadRequest("La cantidad debe ser mayor que cero.");
        if (request.Tipo is not ("AJUSTE_POSITIVO" or "AJUSTE_NEGATIVO" or "MERMA")) return BadRequest("Tipo permitido: AJUSTE_POSITIVO, AJUSTE_NEGATIVO o MERMA.");
        if (!await db.Tanques.AnyAsync(x => x.Id == request.TanqueId && x.Activo)) return NotFound("Tanque no encontrado o inactivo.");
        if (!await db.Usuarios.AnyAsync(x => x.Id == actorId && x.Activo)) return BadRequest("Usuario inválido.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var lockedTank = await db.Tanques.FromSqlInterpolated($"SELECT * FROM tanques WHERE id_tanque={request.TanqueId} FOR UPDATE").AsNoTracking().SingleAsync();
        var stockBefore = lockedTank.ExistenciaActualGalones;
        var movimiento = new MovimientoInventario { TanqueId = request.TanqueId, TipoMovimiento = request.Tipo, CantidadGalones = request.CantidadGalones, ReferenciaTipo = "AJUSTE_MANUAL", Motivo = request.Motivo, UsuarioId = actorId, FechaHora = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified) };
        db.MovimientosInventario.Add(movimiento);
        try
        {
            await db.SaveChangesAsync();
            await db.Entry(movimiento).ReloadAsync();
            var stockAfter = await db.Tanques.AsNoTracking().Where(x => x.Id == request.TanqueId).Select(x => x.ExistenciaActualGalones).SingleAsync();
            await notifications.SincronizarEpisodioInventarioAsync(request.TanqueId, stockAfter);
            await notifications.NotificarAjusteAsync(movimiento);
            await auditoria.RegistrarAsync("INVENTORY_ADJUSTED", "MOVIMIENTO_INVENTARIO", movimiento.Id.ToString(), "EXITO",
                new { tanqueId = request.TanqueId, existenciaGalones = stockBefore },
                new { movimiento.TipoMovimiento, movimiento.CantidadGalones, movimiento.ReferenciaTipo, movimiento.Motivo, existenciaGalones = stockAfter });
            await transaction.CommitAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
            pg.SqlState == PostgresErrorCodes.RaiseException &&
            (pg.MessageText.StartsWith("Inventario insuficiente", StringComparison.Ordinal) || pg.MessageText.StartsWith("El movimiento excede la capacidad", StringComparison.Ordinal) || pg.MessageText.StartsWith("El día operacional ya está cerrado", StringComparison.Ordinal)))
        {
            return Conflict(pg.MessageText);
        }
        await realtime.PublishMovementAsync(movimiento.Id);
        return Created($"api/inventario/movimientos/{movimiento.Id}", movimiento);
    }
}

public record AjusteInventarioRequest(long TanqueId, string Tipo, decimal CantidadGalones, string Motivo, long UsuarioId);
