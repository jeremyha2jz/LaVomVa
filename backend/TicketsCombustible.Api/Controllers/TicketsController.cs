using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using TicketsCombustible.Api.Contracts;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;

namespace TicketsCombustible.Api.Controllers;

[ApiController]
[Route("api/tickets")]
public class TicketsController(TicketsCombustibleDbContext db, IConfiguration configuration, IMemoryCache cache) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar() => Ok(await (
        from ticket in db.Tickets.AsNoTracking()
        join empleado in db.Empleados on ticket.EmpleadoId equals empleado.Id
        join vehiculo in db.Vehiculos on ticket.VehiculoId equals vehiculo.Id
        join departamento in db.Departamentos on ticket.DepartamentoId equals departamento.Id
        join combustible in db.TiposCombustible on ticket.TipoCombustibleId equals combustible.Id
        orderby ticket.FechaCreacion descending
        select new { ticket.Id, ticket.NumeroSecuencial, ticket.Estado, Empleado = empleado.NombreCompleto, Vehiculo = vehiculo.Placa, Departamento = departamento.Nombre, TipoCombustible = combustible.Nombre, ticket.CantidadAutorizadaGalones, ticket.FechaCreacion, ticket.FechaVencimiento }
    ).ToListAsync());

    [HttpGet("{id:guid}")] public async Task<IActionResult> Obtener(Guid id) => await db.Tickets.Where(x => x.Id == id).Select(x => new { x.Id, x.NumeroSecuencial, x.Estado, x.EmpleadoId, x.VehiculoId, x.DepartamentoId, x.TipoCombustibleId, x.CantidadAutorizadaGalones, x.FechaCreacion, x.FechaVencimiento }).SingleOrDefaultAsync() is { } ticket ? Ok(ticket) : NotFound();

    [HttpGet("{id:guid}/qr")]
    [Authorize(Roles = "ADMINISTRADOR,SUPERVISOR")]
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
    [Authorize(Roles = "ADMINISTRADOR,SUPERVISOR")]
    public async Task<IActionResult> Crear(CrearTicketRequest request)
    {
        var solicitud = await db.Solicitudes.FindAsync(request.SolicitudId);
        if (solicitud is null) return NotFound("Solicitud no encontrada.");
        if (solicitud.Estado != EstadoSolicitud.APROBADA || solicitud.CantidadAutorizadaGalones is null || solicitud.FechaVencimiento is null) return Conflict("La solicitud debe estar aprobada y completa.");
        if (await db.Tickets.AnyAsync(x => x.SolicitudId == solicitud.Id)) return Conflict("La solicitud ya tiene un ticket.");
        var id = Guid.NewGuid(); var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var secret = configuration["Qr:SigningSecret"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32 || secret.StartsWith("REEMPLAZA_")) return Problem("Configura Qr:SigningSecret con al menos 32 caracteres privados antes de emitir tickets.");
        var ticket = new Ticket { Id = id, SolicitudId = solicitud.Id, EmpleadoId = solicitud.EmpleadoId, VehiculoId = solicitud.VehiculoId, DepartamentoId = solicitud.DepartamentoId, TipoCombustibleId = solicitud.TipoCombustibleId, CantidadAutorizadaGalones = solicitud.CantidadAutorizadaGalones.Value, FechaCreacion = FechaPg(DateTime.UtcNow), FechaVencimiento = FechaPg(solicitud.FechaVencimiento.Value), Estado = EstadoTicket.CREADO, QrToken = token };
        ticket.QrHash = FirmarTicket(ticket, secret);
        db.Tickets.Add(ticket); await db.SaveChangesAsync(); await db.Entry(ticket).ReloadAsync();
        return CreatedAtAction(nameof(Obtener), new { id = ticket.Id }, ticket);
    }

    [HttpPost("validar")]
    public async Task<IActionResult> Validar(ValidarTicketRequest request)
    {
        var ticket = await db.Tickets.SingleOrDefaultAsync(x => x.QrToken == request.QrData);
        if (ticket is null) return Ok(new { valido = false, estado = "Anulado", ticket = (object?)null, mensajeError = "El ticket no existe o el QR es inválido" });
        var secret = configuration["Qr:SigningSecret"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32 || secret.StartsWith("REEMPLAZA_")) return Problem("Falta configurar el secreto QR.");
        var expectedHash = Encoding.ASCII.GetBytes(FirmarTicket(ticket, secret));
        var storedHash = Encoding.ASCII.GetBytes(ticket.QrHash);
        if (storedHash.Length != expectedHash.Length || !CryptographicOperations.FixedTimeEquals(expectedHash, storedHash)) return Ok(new { valido = false, estado = "Anulado", ticket = (object?)null, mensajeError = "La firma del QR es inválida" });
        var estado = ticket.FechaVencimiento <= DateTime.UtcNow && ticket.Estado is not EstadoTicket.CONSUMIDO and not EstadoTicket.ANULADO ? EstadoTicket.VENCIDO : ticket.Estado;
        if (estado is EstadoTicket.CONSUMIDO or EstadoTicket.ANULADO or EstadoTicket.VENCIDO) return Ok(new { valido = false, estado = NombreEstado(estado), ticket = (object?)null, mensajeError = MensajeEstado(estado) });
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (actorId is not null) cache.Set($"qr-validado:{actorId}:{ticket.Id}", true, TimeSpan.FromMinutes(5));
        var empleado = await db.Empleados.FindAsync(ticket.EmpleadoId); var vehiculo = await db.Vehiculos.FindAsync(ticket.VehiculoId); var departamento = await db.Departamentos.FindAsync(ticket.DepartamentoId); var combustible = await db.TiposCombustible.FindAsync(ticket.TipoCombustibleId);
        return Ok(new { valido = true, estado = NombreEstado(ticket.Estado), ticket = new { id = ticket.NumeroSecuencial, ticketUuid = ticket.Id, empleado = new { codigo = empleado?.CodigoEmpleado, nombre = empleado?.NombreCompleto }, vehiculo = new { placa = vehiculo?.Placa, ficha = vehiculo?.Ficha }, departamento = departamento?.Nombre, cantidadAutorizada = ticket.CantidadAutorizadaGalones, tipoCombustible = combustible?.Nombre, fechaEmision = ticket.FechaCreacion.ToString("yyyy-MM-dd"), fechaVencimiento = ticket.FechaVencimiento.ToString("yyyy-MM-dd") }, mensajeError = (string?)null });
    }

    private static string NombreEstado(EstadoTicket estado) => estado switch { EstadoTicket.CREADO or EstadoTicket.PENDIENTE or EstadoTicket.ENVIADO => "Creado", EstadoTicket.VENCIDO or EstadoTicket.PROXIMO_A_VENCER => "Vencido", EstadoTicket.CONSUMIDO => "Consumido", EstadoTicket.ANULADO => "Anulado", _ => estado.ToString() };
    private static string MensajeEstado(EstadoTicket estado) => estado switch { EstadoTicket.CONSUMIDO => "El ticket ya fue consumido", EstadoTicket.ANULADO => "El ticket fue anulado", EstadoTicket.VENCIDO => "El ticket ya venció", _ => "El ticket no está disponible" };

    private static string FirmarTicket(Ticket ticket, string secret)
    {
        // v2 ties the bearer QR token to every immutable ticket field used for dispatch.
        var payload = string.Join('|', "v2", ticket.Id.ToString("D"),
            ticket.SolicitudId.ToString(CultureInfo.InvariantCulture),
            ticket.EmpleadoId.ToString(CultureInfo.InvariantCulture),
            ticket.VehiculoId.ToString(CultureInfo.InvariantCulture),
            ticket.DepartamentoId.ToString(CultureInfo.InvariantCulture),
            ticket.TipoCombustibleId.ToString(CultureInfo.InvariantCulture),
            ticket.CantidadAutorizadaGalones.ToString("G29", CultureInfo.InvariantCulture),
            (ticket.FechaCreacion.Ticks / 10).ToString(CultureInfo.InvariantCulture),
            (ticket.FechaVencimiento.Ticks / 10).ToString(CultureInfo.InvariantCulture),
            ticket.QrToken);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static DateTime FechaPg(DateTime fecha)
    {
        var utc = fecha.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(fecha, DateTimeKind.Utc) : fecha.ToUniversalTime();
        return DateTime.SpecifyKind(new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc), DateTimeKind.Unspecified);
    }
}

public record ValidarTicketRequest(string QrData);
