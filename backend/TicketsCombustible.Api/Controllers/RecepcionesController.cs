using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;

namespace TicketsCombustible.Api.Controllers;

[ApiController]
[Route("api/recepciones")]
public class RecepcionesController(TicketsCombustibleDbContext db) : ControllerBase
{
    [HttpGet("proveedores")]
    public async Task<IActionResult> Proveedores() => Ok(await db.Proveedores.Where(x => x.Activo).OrderBy(x => x.Nombre).ToListAsync());

    [HttpPost("proveedores")]
    [Authorize(Roles = "ADMINISTRADOR,SUPERVISOR")]
    public async Task<IActionResult> CrearProveedor(Proveedor proveedor)
    {
        if (string.IsNullOrWhiteSpace(proveedor.Nombre)) return BadRequest("El nombre del proveedor es obligatorio.");
        db.Proveedores.Add(proveedor); await db.SaveChangesAsync();
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
        var recepcion = new RecepcionCombustible { ProveedorId = request.ProveedorId, NumeroFactura = request.NumeroFactura, FechaRecepcion = DateTime.SpecifyKind(request.FechaRecepcion, DateTimeKind.Unspecified), UsuarioReceptorId = actorId, Observaciones = request.Observaciones };
        db.Recepciones.Add(recepcion); await db.SaveChangesAsync();
        db.DetallesRecepcion.AddRange(request.Detalles.Select(x => new DetalleRecepcion { RecepcionId = recepcion.Id, TanqueId = x.TanqueId, VolumenRecibidoGalones = x.VolumenRecibidoGalones, CostoUnitario = x.CostoUnitario }));
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return Created($"api/recepciones/{recepcion.Id}", new { recepcion, request.Detalles });
    }
}

public record DetalleRecepcionRequest(long TanqueId, decimal VolumenRecibidoGalones, decimal? CostoUnitario);
public record CrearRecepcionRequest(long ProveedorId, string NumeroFactura, DateTime FechaRecepcion, long UsuarioReceptorId, string? Observaciones, List<DetalleRecepcionRequest> Detalles);
