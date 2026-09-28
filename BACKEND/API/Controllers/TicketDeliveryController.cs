using System.Security.Claims;
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
[Route("api/tickets")]
[Authorize(Roles = "ADMINISTRADOR,SUPERVISOR")]
public sealed class TicketDeliveryController(
    TicketDeliveryService delivery,
    TicketsCombustibleDbContext db,
    IConfiguration configuration) : ControllerBase
{
    [HttpPost("{id:guid}/enviar")]
    public Task<IActionResult> Enviar(Guid id, EnviarTicketRequest request, CancellationToken cancellationToken) => Execute(id, request, false, cancellationToken);

    [HttpPost("{id:guid}/reenviar")]
    public Task<IActionResult> Reenviar(Guid id, EnviarTicketRequest request, CancellationToken cancellationToken) => Execute(id, request, true, cancellationToken);

    [HttpGet("{id:guid}/envios")]
    public async Task<IActionResult> Historial(Guid id, CancellationToken cancellationToken)
    {
        if (!await db.Tickets.AsNoTracking().AnyAsync(x => x.Id == id, cancellationToken)) return NotFound(new ApiErrorResponse("Ticket no encontrado."));
        var rows = await delivery.HistoryAsync(id, cancellationToken);
        return Ok(rows.Select(x => new
        {
            x.Id, x.TicketId, x.Canal, destino = TicketDeliveryService.MaskDestination(x.Destino),
            x.EstadoEnvio, x.DetalleError, x.FechaEnvio, x.SolicitadoEn, x.Intento,
            x.Proveedor, x.Resultado, x.LoteId
        }));
    }

    [HttpPost("{id:guid}/envios/{envioId:long}/reconciliar")]
    public async Task<IActionResult> Reconciliar(Guid id, long envioId, ReconciliarEnvioTicketRequest request, CancellationToken cancellationToken)
    {
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Unauthorized();
        try
        {
            return Ok(await delivery.ReconcileAsync(id, envioId, request.Estado, actorId, cancellationToken));
        }
        catch (TicketDeliveryNotFound ex) { return NotFound(new ApiErrorResponse(ex.Message)); }
        catch (TicketDeliveryInvalid ex) { return BadRequest(new ApiErrorResponse(ex.Message)); }
        catch (TicketDeliveryConflict ex) { return Conflict(new ApiErrorResponse(ex.Message)); }
    }

    [AllowAnonymous]
    [HttpGet("public/qr")]
    public async Task<IActionResult> PublicQr([FromQuery] string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128) return NotFound();
        var png = await delivery.PublicQrAsync(token, configuration["Qr:SigningSecret"], cancellationToken);
        if (png is null) return NotFound(new ApiErrorResponse("El QR no está disponible."));
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return File(png, "image/png", "ticket-qr.png");
    }

    private async Task<IActionResult> Execute(Guid id, EnviarTicketRequest request, bool retry, CancellationToken cancellationToken)
    {
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Unauthorized();
        try
        {
            var outcome = await delivery.SendAsync(id, request.Canal, request.IdempotencyKey, retry, actorId,
                configuration["Qr:SigningSecret"], cancellationToken);
            return Ok(outcome);
        }
        catch (TicketDeliveryNotFound ex) { return NotFound(new ApiErrorResponse(ex.Message)); }
        catch (TicketDeliveryInvalid ex) { return BadRequest(new ApiErrorResponse(ex.Message)); }
        catch (TicketDeliveryConflict ex) { return Conflict(new ApiErrorResponse(ex.Message)); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
            (pg.SqlState == PostgresErrorCodes.UniqueViolation ||
             pg.SqlState == PostgresErrorCodes.RaiseException && pg.MessageText.StartsWith("Transición inválida de ticket", StringComparison.Ordinal)))
        {
            return Conflict(new ApiErrorResponse("Ya existe un envío pendiente o la transición del ticket cambió en otra solicitud."));
        }
    }
}
