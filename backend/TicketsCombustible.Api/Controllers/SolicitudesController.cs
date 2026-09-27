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
    [HttpGet("{id:long}")] public async Task<IActionResult> Obtener(long id) => await db.Solicitudes.FindAsync(id) is { } solicitud ? Ok(solicitud) : NotFound(new ApiErrorResponse("Solicitud no encontrada."));

    [HttpPost]
    public async Task<IActionResult> Crear(CrearSolicitudRequest request)
    {
        if (request.CantidadSolicitadaGalones <= 0) return BadRequest(new ApiErrorResponse("La cantidad solicitada debe ser mayor que cero."));
        DateTime? fechaVencimiento = request.FechaVencimiento is { } vence ? FechaSinZona(vence) : null;
        if (fechaVencimiento <= DateTime.UtcNow) return BadRequest(new ApiErrorResponse("La fecha de vencimiento debe ser futura."));
        if (!await db.Empleados.AnyAsync(x => x.Id == request.EmpleadoId && x.Activo) || !await db.Vehiculos.AnyAsync(x => x.Id == request.VehiculoId && x.Activo) || !await db.Departamentos.AnyAsync(x => x.Id == request.DepartamentoId && x.Activo) || !await db.TiposCombustible.AnyAsync(x => x.Id == request.TipoCombustibleId && x.Activo)) return BadRequest(new ApiErrorResponse("Selecciona un empleado, vehículo, departamento y combustible válidos."));
        var solicitud = new SolicitudCombustible { EmpleadoId = request.EmpleadoId, VehiculoId = request.VehiculoId, DepartamentoId = request.DepartamentoId, TipoCombustibleId = request.TipoCombustibleId, CantidadSolicitadaGalones = request.CantidadSolicitadaGalones, FechaVencimiento = fechaVencimiento, UsuarioCreadorId = request.UsuarioCreadorId, TipoSolicitud = request.TipoSolicitud, Motivo = request.Motivo, Estado = EstadoSolicitud.PENDIENTE };
        db.Solicitudes.Add(solicitud); await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Obtener), new { id = solicitud.Id }, solicitud);
    }

    [HttpPut("{id:long}/aprobar")]
    public async Task<IActionResult> Aprobar(long id, AprobarSolicitudRequest request)
    {
        var solicitud = await db.Solicitudes.FindAsync(id);
        if (solicitud is null) return NotFound(new ApiErrorResponse("Solicitud no encontrada."));
        if (solicitud.Estado != EstadoSolicitud.PENDIENTE) return Conflict(new ApiErrorResponse("Solo se puede aprobar una solicitud pendiente."));
        if (request.CantidadAutorizadaGalones <= 0 || request.CantidadAutorizadaGalones > solicitud.CantidadSolicitadaGalones) return BadRequest(new ApiErrorResponse("La cantidad autorizada debe ser válida."));
        var fechaVencimiento = FechaSinZona(request.FechaVencimiento);
        if (fechaVencimiento <= DateTime.UtcNow) return BadRequest(new ApiErrorResponse("La fecha de vencimiento debe ser futura."));
        solicitud.CantidadAutorizadaGalones = request.CantidadAutorizadaGalones; solicitud.FechaVencimiento = fechaVencimiento; solicitud.UsuarioAprobadorId = request.UsuarioAprobadorId; solicitud.FechaAprobacion = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified); solicitud.Estado = EstadoSolicitud.APROBADA;
        await db.SaveChangesAsync(); return Ok(solicitud);
    }

    [HttpPut("{id:long}/rechazar")]
    public async Task<IActionResult> Rechazar(long id)
    {
        var solicitud = await db.Solicitudes.FindAsync(id);
        if (solicitud is null) return NotFound(new ApiErrorResponse("Solicitud no encontrada."));
        if (solicitud.Estado != EstadoSolicitud.PENDIENTE) return Conflict(new ApiErrorResponse("Solo se puede rechazar una solicitud pendiente."));
        solicitud.Estado = EstadoSolicitud.RECHAZADA;
        await db.SaveChangesAsync();
        return Ok(solicitud);
    }

    // Las columnas son "timestamp without time zone" y guardan la hora en UTC: una fecha con zona (ej. "...Z" o "-04:00") se pasa a UTC antes de quitarle la zona, para no guardar la hora local como si fuera UTC.
    private static DateTime FechaSinZona(DateTime fecha) => DateTime.SpecifyKind(fecha.Kind == DateTimeKind.Unspecified ? fecha : fecha.ToUniversalTime(), DateTimeKind.Unspecified);
}
