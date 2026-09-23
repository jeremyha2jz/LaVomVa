using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using TicketsCombustible.Api.Contracts;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;

namespace TicketsCombustible.Api.Controllers;

[ApiController]
[Route("api/tickets")]
public class TicketsController(TicketsCombustibleDbContext db, IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar() => Ok((await db.Tickets.OrderByDescending(x => x.FechaCreacion).Select(x => new { id = x.NumeroSecuencial, estado = x.Estado, vehiculo = db.Vehiculos.Where(v => v.Id == x.VehiculoId).Select(v => v.Placa).FirstOrDefault(), cantidadAutorizada = x.CantidadAutorizadaGalones, x.FechaVencimiento }).ToListAsync()).Select(x => new { x.id, estado = NombreEstado(x.estado), x.vehiculo, x.cantidadAutorizada, fechaVencimiento = x.FechaVencimiento.ToString("yyyy-MM-dd") }));

    [HttpGet("{id:guid}")] public async Task<IActionResult> Obtener(Guid id) => await db.Tickets.FindAsync(id) is { } ticket ? Ok(ticket) : NotFound();

    [HttpGet("{id:guid}/qr")]
    public async Task<IActionResult> ObtenerQr(Guid id)
    {
        var ticket = await db.Tickets.FindAsync(id);
        if (ticket is null) return NotFound("Ticket no encontrado.");
        using var generador = new QRCodeGenerator();
        using var datos = generador.CreateQrCode(ticket.QrToken, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(datos).GetGraphic(12);
        return File(png, "image/png");
    }

    [HttpPost]
    public async Task<IActionResult> Crear(CrearTicketRequest request)
    {
        var solicitud = await db.Solicitudes.FindAsync(request.SolicitudId);
        if (solicitud is null) return NotFound("Solicitud no encontrada.");
        if (solicitud.Estado != EstadoSolicitud.APROBADA || solicitud.CantidadAutorizadaGalones is null || solicitud.FechaVencimiento is null) return Conflict("La solicitud debe estar aprobada y completa.");
        if (await db.Tickets.AnyAsync(x => x.SolicitudId == solicitud.Id)) return Conflict("La solicitud ya tiene un ticket.");
        var id = Guid.NewGuid(); var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var secret = configuration["Qr:SigningSecret"] ?? "CAMBIAR-EN-DESARROLLO";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{id}|{solicitud.Id}|{token}|{secret}"))).ToLowerInvariant();
        var ticket = new Ticket { Id = id, SolicitudId = solicitud.Id, EmpleadoId = solicitud.EmpleadoId, VehiculoId = solicitud.VehiculoId, DepartamentoId = solicitud.DepartamentoId, TipoCombustibleId = solicitud.TipoCombustibleId, CantidadAutorizadaGalones = solicitud.CantidadAutorizadaGalones.Value, FechaVencimiento = solicitud.FechaVencimiento.Value, Estado = EstadoTicket.CREADO, QrToken = token, QrHash = hash };
        db.Tickets.Add(ticket); await db.SaveChangesAsync(); await db.Entry(ticket).ReloadAsync();
        return CreatedAtAction(nameof(Obtener), new { id = ticket.Id }, ticket);
    }

    [HttpPost("validar")]
    public async Task<IActionResult> Validar(ValidarTicketRequest request)
    {
        var ticket = await db.Tickets.SingleOrDefaultAsync(x => x.QrToken == request.QrData);
        if (ticket is null) return Ok(new { valido = false, estado = "Anulado", ticket = (object?)null, mensajeError = "El ticket no existe o el QR es inválido" });
        var estado = ticket.FechaVencimiento <= DateTime.UtcNow && ticket.Estado is not EstadoTicket.CONSUMIDO and not EstadoTicket.ANULADO ? EstadoTicket.VENCIDO : ticket.Estado;
        if (estado is EstadoTicket.CONSUMIDO or EstadoTicket.ANULADO or EstadoTicket.VENCIDO) return Ok(new { valido = false, estado = NombreEstado(estado), ticket = (object?)null, mensajeError = MensajeEstado(estado) });
        var empleado = await db.Empleados.FindAsync(ticket.EmpleadoId); var vehiculo = await db.Vehiculos.FindAsync(ticket.VehiculoId); var departamento = await db.Departamentos.FindAsync(ticket.DepartamentoId); var combustible = await db.TiposCombustible.FindAsync(ticket.TipoCombustibleId);
        return Ok(new { valido = true, estado = NombreEstado(ticket.Estado), ticket = new { id = ticket.NumeroSecuencial, ticketUuid = ticket.Id, empleado = new { codigo = empleado?.CodigoEmpleado, nombre = empleado?.NombreCompleto }, vehiculo = new { placa = vehiculo?.Placa, ficha = vehiculo?.Ficha }, departamento = departamento?.Nombre, cantidadAutorizada = ticket.CantidadAutorizadaGalones, tipoCombustible = combustible?.Nombre, fechaEmision = ticket.FechaCreacion.ToString("yyyy-MM-dd"), fechaVencimiento = ticket.FechaVencimiento.ToString("yyyy-MM-dd") }, mensajeError = (string?)null });
    }

    private static string NombreEstado(EstadoTicket estado) => estado switch { EstadoTicket.CREADO or EstadoTicket.PENDIENTE or EstadoTicket.ENVIADO => "Creado", EstadoTicket.VENCIDO or EstadoTicket.PROXIMO_A_VENCER => "Vencido", EstadoTicket.CONSUMIDO => "Consumido", EstadoTicket.ANULADO => "Anulado", _ => estado.ToString() };
    private static string MensajeEstado(EstadoTicket estado) => estado switch { EstadoTicket.CONSUMIDO => "El ticket ya fue consumido", EstadoTicket.ANULADO => "El ticket fue anulado", EstadoTicket.VENCIDO => "El ticket ya venció", _ => "El ticket no está disponible" };
}

public record ValidarTicketRequest(string QrData);
