using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;
using TicketsCombustible.Api.Services;

namespace TicketsCombustible.Api.Controllers;

[ApiController]
[Authorize(Roles = "ADMINISTRADOR,AUDITOR")]
[Route("api/auditoria")]
public sealed class AuditoriaController(TicketsCombustibleDbContext db, IAuditoriaService auditoria) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Consultar(
        [FromQuery] string? accion,
        [FromQuery] string? entidad,
        [FromQuery] string? entidadId,
        [FromQuery] string? resultado,
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = 50,
        CancellationToken cancellationToken = default)
    {
        if (pagina < 1 || tamano is < 1 or > 100) return BadRequest(new ApiErrorResponse("La página debe ser positiva y el tamaño debe estar entre 1 y 100."));
        if (resultado is not null && resultado is not ("EXITO" or "FALLO")) return BadRequest(new ApiErrorResponse("Resultado debe ser EXITO o FALLO."));
        if (desde.HasValue && hasta.HasValue && desde > hasta) return BadRequest(new ApiErrorResponse("El rango de fechas no es válido."));

        var query = db.Auditoria.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(accion)) query = query.Where(x => x.Accion == accion);
        if (!string.IsNullOrWhiteSpace(entidad)) query = query.Where(x => x.Entidad == entidad);
        if (!string.IsNullOrWhiteSpace(entidadId)) query = query.Where(x => x.EntidadId == entidadId);
        if (!string.IsNullOrWhiteSpace(resultado)) query = query.Where(x => x.Resultado == resultado);
        if (desde.HasValue) query = query.Where(x => x.FechaHora >= AsUtcDatabaseTime(desde.Value));
        if (hasta.HasValue) query = query.Where(x => x.FechaHora <= AsUtcDatabaseTime(hasta.Value));

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.Id).Skip((pagina - 1) * tamano).Take(tamano).ToListAsync(cancellationToken);
        var userId = long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId) ? parsedUserId : (long?)null;
        await auditoria.RegistrarAsync("AUDIT_LOGS_READ", "AUDITORIA", null, "EXITO", new { pagina, tamano }, new { coincidencias = total }, usuarioId: userId, cancellationToken: cancellationToken);
        return Ok(new { pagina, tamano, total, items });
    }

    private static DateTime AsUtcDatabaseTime(DateTime value) =>
        DateTime.SpecifyKind(value.Kind == DateTimeKind.Unspecified ? value : value.ToUniversalTime(), DateTimeKind.Unspecified);
}
