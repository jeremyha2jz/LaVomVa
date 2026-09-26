using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TicketsCombustible.Api.Contracts;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;
using Npgsql;

namespace TicketsCombustible.Api.Controllers;

[ApiController]
[Authorize(Roles = "ADMINISTRADOR,DESPACHADOR")]
[Route("api/despachos")]
public class DespachosController(TicketsCombustibleDbContext db, IMemoryCache cache) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Registrar(RegistrarDespachoRequest request)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var ticket = Guid.TryParse(request.TicketId, out var ticketUuid)
            ? await db.Tickets.SingleOrDefaultAsync(x => x.Id == ticketUuid)
            : await db.Tickets.SingleOrDefaultAsync(x => x.NumeroSecuencial == request.TicketId);
        if (ticket is null) return NotFound("Ticket no encontrado.");
        if (ticket.Estado is EstadoTicket.ANULADO or EstadoTicket.CONSUMIDO || ticket.FechaVencimiento <= DateTime.UtcNow) return Conflict("El ticket no está disponible para despacho.");
        var operadorId = long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var idUsuario) ? idUsuario : 0;
        if (operadorId == 0) return BadRequest("Debe iniciar sesión o indicar el operador.");
        var validationKey = $"qr-validado:{operadorId}:{ticket.Id}";
        if (!cache.TryGetValue(validationKey, out _)) return BadRequest("Escanea y valida el QR antes del despacho.");
        if (request.IdentidadConfirmada is not true) return BadRequest("Debe confirmar la identidad antes del despacho.");
        if (request.GalonesServidos <= 0 || request.GalonesServidos > ticket.CantidadAutorizadaGalones) return BadRequest("Los galones deben ser válidos y no superar lo autorizado.");
        Tanque? tanque;
        if (request.TanqueId.HasValue) tanque = await db.Tanques.SingleOrDefaultAsync(x => x.Id == request.TanqueId && x.Activo);
        else
        {
            var compatibles = await db.Tanques.Where(x => x.Activo && x.TipoCombustibleId == ticket.TipoCombustibleId).Take(2).ToListAsync();
            if (compatibles.Count != 1) return Conflict("Debe seleccionar un tanque: hay cero o varios tanques compatibles.");
            tanque = compatibles[0];
        }
        if (tanque is null) return NotFound("Tanque no encontrado o inactivo.");
        var estacionId = request.EstacionId ?? tanque.EstacionId;
        if (tanque.EstacionId != estacionId || tanque.TipoCombustibleId != ticket.TipoCombustibleId) return BadRequest("El tanque no corresponde a la estación o tipo de combustible del ticket.");
        if (tanque.ExistenciaActualGalones < request.GalonesServidos) return Conflict("Inventario insuficiente.");
        var despacho = new Despacho { TicketId = ticket.Id, TanqueId = tanque.Id, EstacionId = estacionId, OperadorId = operadorId, GalonesServidos = request.GalonesServidos, OdometroKm = request.OdometroKm, IdentidadConfirmada = true, Observaciones = request.Observaciones };
        db.Despachos.Add(despacho);
        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
            pg.SqlState == PostgresErrorCodes.UniqueViolation && pg.ConstraintName == "despachos_id_ticket_key")
        {
            return Conflict("El ticket ya fue despachado por otro operador.");
        }
        cache.Remove(validationKey);
        return Ok(new { ok = true, mensaje = "Despacho registrado", despacho });
    }
}
