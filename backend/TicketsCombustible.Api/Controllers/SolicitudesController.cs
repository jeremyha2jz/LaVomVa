using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketsCombustible.Api.Contracts;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;

namespace TicketsCombustible.Api.Controllers;

[ApiController]
[Route("api/solicitudes")]
public class SolicitudesController(TicketsCombustibleDbContext db) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> Listar() => Ok(await db.Solicitudes.OrderByDescending(x => x.Id).ToListAsync());
    [HttpGet("{id:long}")] public async Task<IActionResult> Obtener(long id) => await db.Solicitudes.FindAsync(id) is { } solicitud ? Ok(solicitud) : NotFound();

    [HttpPost]
    public async Task<IActionResult> Crear(CrearSolicitudRequest request)
    {
        if (request.CantidadSolicitadaGalones <= 0) return BadRequest("La cantidad solicitada debe ser mayor que cero.");
        var solicitud = new SolicitudCombustible { EmpleadoId = request.EmpleadoId, VehiculoId = request.VehiculoId, DepartamentoId = request.DepartamentoId, TipoCombustibleId = request.TipoCombustibleId, CantidadSolicitadaGalones = request.CantidadSolicitadaGalones, FechaVencimiento = request.FechaVencimiento, UsuarioCreadorId = request.UsuarioCreadorId, Estado = EstadoSolicitud.PENDIENTE };
        db.Solicitudes.Add(solicitud); await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Obtener), new { id = solicitud.Id }, solicitud);
    }

    [HttpPut("{id:long}/aprobar")]
    public async Task<IActionResult> Aprobar(long id, AprobarSolicitudRequest request)
    {
        var solicitud = await db.Solicitudes.FindAsync(id);
        if (solicitud is null) return NotFound();
        if (solicitud.Estado != EstadoSolicitud.PENDIENTE) return Conflict("Solo se puede aprobar una solicitud pendiente.");
        if (request.CantidadAutorizadaGalones <= 0 || request.CantidadAutorizadaGalones > solicitud.CantidadSolicitadaGalones) return BadRequest("La cantidad autorizada debe ser válida.");
        if (request.FechaVencimiento <= DateTime.UtcNow) return BadRequest("La fecha de vencimiento debe ser futura.");
        solicitud.CantidadAutorizadaGalones = request.CantidadAutorizadaGalones; solicitud.FechaVencimiento = request.FechaVencimiento; solicitud.UsuarioAprobadorId = request.UsuarioAprobadorId; solicitud.FechaAprobacion = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified); solicitud.Estado = EstadoSolicitud.APROBADA;
        await db.SaveChangesAsync(); return Ok(solicitud);
    }
}
