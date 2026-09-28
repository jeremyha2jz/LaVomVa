using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;
using TicketsCombustible.Api.Services;

namespace TicketsCombustible.Api.Controllers;

[ApiController]
[Authorize(Roles = "ADMINISTRADOR,SUPERVISOR,DESPACHADOR,AUDITOR")]
[Route("api/notificaciones")]
public sealed class NotificacionesController(TicketsCombustibleDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] string? tipo, [FromQuery] bool? leida,
        [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, [FromQuery] int pagina = 1,
        [FromQuery] int tamano = 30, CancellationToken cancellationToken = default)
    {
        if (!TryUserId(out var userId)) return Unauthorized();
        if (pagina < 1 || tamano is < 1 or > 100) return BadRequest(new ApiErrorResponse("La página debe ser positiva y el tamaño debe estar entre 1 y 100."));
        if (desde.HasValue && hasta.HasValue && desde > hasta) return BadRequest(new ApiErrorResponse("El rango de fechas no es válido."));
        var query = db.Notificaciones.AsNoTracking().Where(x => x.UsuarioId == userId);
        if (!string.IsNullOrWhiteSpace(tipo)) query = query.Where(x => x.Tipo == tipo);
        if (leida.HasValue) query = query.Where(x => (x.Estado == "LEIDA") == leida.Value);
        if (desde.HasValue) query = query.Where(x => x.FechaCreacion >= UtcDb(desde.Value));
        if (hasta.HasValue) query = query.Where(x => x.FechaCreacion <= UtcDb(hasta.Value));
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.FechaCreacion).ThenByDescending(x => x.Id)
            .Skip((pagina - 1) * tamano).Take(tamano).Select(x => new
            {
                id = x.Id, tipo = x.Tipo, titulo = x.Titulo, mensaje = x.Mensaje, severidad = x.Severidad,
                usuarioId = x.UsuarioId, referenciaTipo = x.ReferenciaTipo, referenciaId = x.ReferenciaId,
                fechaCreacion = x.FechaCreacion, fechaLectura = x.FechaLectura, leida = x.Estado == "LEIDA", metadata = x.Metadata
            }).ToListAsync(cancellationToken);
        return Ok(new { pagina, tamano, total, items });
    }

    [HttpGet("no-leidas")]
    public async Task<IActionResult> NoLeidas(CancellationToken cancellationToken)
    {
        if (!TryUserId(out var userId)) return Unauthorized();
        var cantidad = await db.Notificaciones.CountAsync(x => x.UsuarioId == userId && x.Estado != "LEIDA", cancellationToken);
        return Ok(new { cantidad });
    }

    [HttpPost("{id:long}/leer")]
    public async Task<IActionResult> MarcarLeida(long id, CancellationToken cancellationToken)
    {
        if (!TryUserId(out var userId)) return Unauthorized();
        var row = await db.Notificaciones.SingleOrDefaultAsync(x => x.Id == id && x.UsuarioId == userId, cancellationToken);
        if (row is null) return NotFound(new ApiErrorResponse("Notificación no encontrada."));
        if (row.Estado != "LEIDA")
        {
            row.Estado = "LEIDA";
            row.FechaLectura = UtcDb(clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(cancellationToken);
        }
        return Ok(new { id = row.Id, leida = true, fechaLectura = row.FechaLectura });
    }

    [HttpPost("leer-todas")]
    public async Task<IActionResult> MarcarTodasLeidas(CancellationToken cancellationToken)
    {
        if (!TryUserId(out var userId)) return Unauthorized();
        var now = UtcDb(clock.GetUtcNow().UtcDateTime);
        var actualizadas = await db.Notificaciones.Where(x => x.UsuarioId == userId && x.Estado != "LEIDA")
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Estado, "LEIDA").SetProperty(x => x.FechaLectura, now), cancellationToken);
        return Ok(new { actualizadas });
    }

    private bool TryUserId(out long id) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out id);
    private static DateTime UtcDb(DateTime value) => DateTime.SpecifyKind(value.Kind == DateTimeKind.Unspecified ? value : value.ToUniversalTime(), DateTimeKind.Unspecified);
}
