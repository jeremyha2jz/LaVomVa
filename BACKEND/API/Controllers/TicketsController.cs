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
using TicketsCombustible.Api.Services;
using Npgsql;

namespace TicketsCombustible.Api.Controllers;

[ApiController]
[Route("api/tickets")]
public class TicketsController(TicketsCombustibleDbContext db, IConfiguration configuration, IMemoryCache cache, IAuditoriaService auditoria, TicketLifecycleService lifecycle) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var rows = await (
        from ticket in db.Tickets.AsNoTracking()
        join empleado in db.Empleados on ticket.EmpleadoId equals empleado.Id
        join vehiculo in db.Vehiculos on ticket.VehiculoId equals vehiculo.Id
        join departamento in db.Departamentos on ticket.DepartamentoId equals departamento.Id
        join combustible in db.TiposCombustible on ticket.TipoCombustibleId equals combustible.Id
        orderby ticket.FechaCreacion descending
        select new { Ticket = ticket, Empleado = empleado.NombreCompleto, Vehiculo = vehiculo.Placa, Departamento = departamento.Nombre, TipoCombustible = combustible.Nombre }
        ).ToListAsync();
        return Ok(rows.Select(x => new { x.Ticket.Id, x.Ticket.NumeroSecuencial, Estado = lifecycle.EstadoActual(x.Ticket), x.Empleado, x.Vehiculo, x.Departamento, x.TipoCombustible, x.Ticket.CantidadAutorizadaGalones, x.Ticket.FechaCreacion, x.Ticket.FechaVencimiento, x.Ticket.AnuladoEn, x.Ticket.MotivoAnulacion }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obtener(Guid id)
    {
        var ticket = await db.Tickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        return ticket is null ? NotFound(new ApiErrorResponse("Ticket no encontrado.")) : Ok(new { ticket.Id, ticket.NumeroSecuencial, Estado = lifecycle.EstadoActual(ticket), ticket.EmpleadoId, ticket.VehiculoId, ticket.DepartamentoId, ticket.TipoCombustibleId, ticket.CantidadAutorizadaGalones, ticket.FechaCreacion, ticket.FechaVencimiento, ticket.AnuladoEn, ticket.MotivoAnulacion });
    }

    [HttpGet("{id:guid}/despacho")]
    public async Task<IActionResult> ObtenerDespacho(Guid id)
    {
        if (!await db.Tickets.AsNoTracking().AnyAsync(x => x.Id == id))
            return NotFound(new ApiErrorResponse("Ticket no encontrado."));

        var despacho = await (
            from registro in db.Despachos.AsNoTracking()
            join operador in db.Usuarios on registro.OperadorId equals operador.Id
            join tanque in db.Tanques on registro.TanqueId equals tanque.Id
            join estacion in db.Estaciones on registro.EstacionId equals estacion.Id
            where registro.TicketId == id
            select new
            {
                registro.Id,
                registro.TicketId,
                registro.GalonesServidos,
                registro.Observaciones,
                registro.FechaHora,
                registro.IdentidadConfirmada,
                Operador = new { operador.Id, Nombre = operador.NombreCompleto },
                Tanque = new { tanque.Id, tanque.Codigo, tanque.Nombre },
                Estacion = new { estacion.Id, estacion.Nombre }
            }).SingleOrDefaultAsync();

        return despacho is null
            ? NotFound(new ApiErrorResponse("El ticket todavía no tiene un despacho registrado."))
            : Ok(despacho);
    }

    [HttpPost("{id:guid}/anular")]
    [Authorize(Roles = "ADMINISTRADOR,SUPERVISOR")]
    public async Task<IActionResult> Anular(Guid id, AnularTicketRequest request)
    {
        var motivo = request.Motivo?.Trim();
        if (string.IsNullOrWhiteSpace(motivo)) return BadRequest(new ApiErrorResponse("El motivo de anulación es obligatorio."));
        if (motivo.Length > 500) return BadRequest(new ApiErrorResponse("El motivo de anulación no puede superar 500 caracteres."));
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Unauthorized();

        await using var transaction = await db.Database.BeginTransactionAsync();
        var ticket = await db.Tickets.FromSqlInterpolated($"SELECT * FROM tickets WHERE id_ticket = {id} FOR UPDATE").SingleOrDefaultAsync();
        if (ticket is null) return NotFound(new ApiErrorResponse("Ticket no encontrado."));

        var estadoAnterior = lifecycle.EstadoActual(ticket);
        if (!lifecycle.PuedeTransicionar(estadoAnterior, EstadoTicket.ANULADO))
            return Conflict(new ApiErrorResponse($"No se puede anular un ticket en estado {estadoAnterior}."));

        var antes = new { estado = estadoAnterior.ToString(), ticket.AnuladoEn, ticket.MotivoAnulacion };
        ticket.Estado = EstadoTicket.ANULADO;
        ticket.UsuarioAnulacionId = actorId;
        ticket.AnuladoEn = FechaPg(lifecycle.UtcNow);
        ticket.MotivoAnulacion = motivo;
        try
        {
            await db.SaveChangesAsync();
            await auditoria.RegistrarAsync("TICKET_ANULADO", "TICKET", ticket.Id.ToString("D"), "EXITO", antes,
                new { estado = EstadoTicket.ANULADO.ToString(), usuarioAnulacionId = actorId, ticket.AnuladoEn, motivoAnulacion = motivo });
            await transaction.CommitAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
            pg.SqlState == PostgresErrorCodes.RaiseException && pg.MessageText.StartsWith("Transición inválida de ticket", StringComparison.Ordinal))
        {
            return Conflict(new ApiErrorResponse("El estado del ticket cambió y ya no permite la anulación."));
        }

        return Ok(new { ticket.Id, ticket.NumeroSecuencial, Estado = EstadoTicket.ANULADO, ticket.AnuladoEn, ticket.MotivoAnulacion });
    }

    [HttpGet("{id:guid}/qr")]
    [Authorize(Roles = "ADMINISTRADOR,SUPERVISOR")]
    public async Task<IActionResult> ObtenerQr(Guid id)
    {
        var ticket = await db.Tickets.FindAsync(id);
        if (ticket is null) return NotFound(new ApiErrorResponse("Ticket no encontrado."));
        using var generador = new QRCodeGenerator();
        using var datos = generador.CreateQrCode(ticket.QrToken, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(datos).GetGraphic(12);
        await auditoria.RegistrarAsync("TICKET_QR_READ", "TICKET", ticket.NumeroSecuencial, "EXITO", detalle: "Se descargó la imagen QR; no se registra el contenido QR.");
        return File(png, "image/png");
    }

    [HttpPost]
    [Authorize(Roles = "ADMINISTRADOR,SUPERVISOR")]
    public async Task<IActionResult> Crear(CrearTicketRequest request)
    {
        var solicitud = await db.Solicitudes.FindAsync(request.SolicitudId);
        if (solicitud is null) return NotFound(new ApiErrorResponse("Solicitud no encontrada."));
        if (solicitud.Estado != EstadoSolicitud.APROBADA || solicitud.CantidadAutorizadaGalones is null || solicitud.FechaVencimiento is null) return Conflict(new ApiErrorResponse("La solicitud debe estar aprobada y completa."));
        if (await db.Tickets.AnyAsync(x => x.SolicitudId == solicitud.Id)) return Conflict(new ApiErrorResponse("La solicitud ya tiene un ticket."));
        var id = Guid.NewGuid(); var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var secret = configuration["Qr:SigningSecret"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32 || secret.StartsWith("REEMPLAZA_")) return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse("Configura Qr:SigningSecret con al menos 32 caracteres privados antes de emitir tickets."));
        var ticket = new Ticket { Id = id, SolicitudId = solicitud.Id, EmpleadoId = solicitud.EmpleadoId, VehiculoId = solicitud.VehiculoId, DepartamentoId = solicitud.DepartamentoId, TipoCombustibleId = solicitud.TipoCombustibleId, CantidadAutorizadaGalones = solicitud.CantidadAutorizadaGalones.Value, FechaCreacion = FechaPg(DateTime.UtcNow), FechaVencimiento = FechaPg(solicitud.FechaVencimiento.Value), Estado = EstadoTicket.CREADO, QrToken = token };
        ticket.QrHash = TicketQrSignature.Sign(ticket, secret);
        db.Tickets.Add(ticket);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        await db.Entry(ticket).ReloadAsync();
        await auditoria.RegistrarAsync("TICKET_ISSUED", "TICKET", ticket.NumeroSecuencial, "EXITO", datosNuevos: Snapshot(ticket));
        await transaction.CommitAsync();
        return CreatedAtAction(nameof(Obtener), new { id = ticket.Id }, new
        {
            ticket.Id,
            ticket.NumeroSecuencial,
            Estado = lifecycle.EstadoActual(ticket),
            ticket.EmpleadoId,
            ticket.VehiculoId,
            ticket.DepartamentoId,
            ticket.TipoCombustibleId,
            ticket.CantidadAutorizadaGalones,
            ticket.FechaCreacion,
            ticket.FechaVencimiento
        });
    }

    [HttpPost("validar")]
    public async Task<IActionResult> Validar(ValidarTicketRequest request)
    {
        var ticket = await db.Tickets.SingleOrDefaultAsync(x => x.QrToken == request.QrData);
        if (ticket is null)
        {
            await auditoria.RegistrarAsync("QR_VALIDATED", "TICKET", null, "FALLO", detalle: "QR inválido o ticket inexistente.");
            return Ok(new { valido = false, estado = "Anulado", ticket = (object?)null, mensajeError = "El ticket no existe o el QR es inválido" });
        }
        var secret = configuration["Qr:SigningSecret"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32 || secret.StartsWith("REEMPLAZA_"))
        {
            await auditoria.RegistrarAsync("QR_VALIDATED", "TICKET", ticket.NumeroSecuencial, "FALLO", detalle: "No está configurado el firmador QR.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse("Falta configurar el secreto QR."));
        }
        if (!TicketQrSignature.Verify(ticket, secret))
        {
            await auditoria.RegistrarAsync("QR_VALIDATED", "TICKET", ticket.NumeroSecuencial, "FALLO", detalle: "La firma QR es inválida.");
            return Ok(new { valido = false, estado = "Anulado", ticket = (object?)null, mensajeError = "La firma del QR es inválida" });
        }
        var estado = lifecycle.EstadoActual(ticket);
        if (!lifecycle.PuedeTransicionar(estado, EstadoTicket.CONSUMIDO))
        {
            await auditoria.RegistrarAsync("QR_VALIDATED", "TICKET", ticket.NumeroSecuencial, "FALLO", datosAnteriores: new { estado = ticket.Estado.ToString() }, datosNuevos: new { estado = estado.ToString() }, detalle: MensajeEstado(estado));
            return Ok(new { valido = false, estado = NombreEstado(estado), ticket = (object?)null, mensajeError = MensajeEstado(estado) });
        }
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (actorId is not null) cache.Set($"qr-validado:{actorId}:{ticket.Id}", true, TimeSpan.FromMinutes(5));
        var empleado = await db.Empleados.FindAsync(ticket.EmpleadoId); var vehiculo = await db.Vehiculos.FindAsync(ticket.VehiculoId); var departamento = await db.Departamentos.FindAsync(ticket.DepartamentoId); var combustible = await db.TiposCombustible.FindAsync(ticket.TipoCombustibleId);
        await auditoria.RegistrarAsync("QR_VALIDATED", "TICKET", ticket.NumeroSecuencial, "EXITO", detalle: "QR válido; no se registra el token.");
        return Ok(new { valido = true, estado = NombreEstado(estado), ticket = new { id = ticket.NumeroSecuencial, ticketUuid = ticket.Id, empleado = new { codigo = empleado?.CodigoEmpleado, nombre = empleado?.NombreCompleto }, vehiculo = new { placa = vehiculo?.Placa, ficha = vehiculo?.Ficha }, departamento = departamento?.Nombre, cantidadAutorizada = ticket.CantidadAutorizadaGalones, tipoCombustible = combustible?.Nombre, fechaEmision = ticket.FechaCreacion.ToString("yyyy-MM-dd"), fechaVencimiento = ticket.FechaVencimiento.ToString("yyyy-MM-dd") }, mensajeError = (string?)null });
    }

    private static object Snapshot(Ticket ticket) => new
    {
        ticket.Id, ticket.SolicitudId, ticket.NumeroSecuencial, ticket.EmpleadoId,
        ticket.VehiculoId, ticket.DepartamentoId, ticket.TipoCombustibleId,
        ticket.CantidadAutorizadaGalones, ticket.FechaCreacion, ticket.FechaVencimiento,
        estado = ticket.Estado.ToString()
    };

    private static string NombreEstado(EstadoTicket estado) => estado switch { EstadoTicket.CREADO or EstadoTicket.PENDIENTE or EstadoTicket.ENVIADO => "Creado", EstadoTicket.PROXIMO_A_VENCER => "Proximo a vencer", EstadoTicket.VENCIDO => "Vencido", EstadoTicket.CONSUMIDO => "Consumido", EstadoTicket.ANULADO => "Anulado", _ => estado.ToString() };
    private static string MensajeEstado(EstadoTicket estado) => estado switch { EstadoTicket.CONSUMIDO => "El ticket ya fue consumido", EstadoTicket.ANULADO => "El ticket fue anulado", EstadoTicket.VENCIDO => "El ticket ya venció", _ => "El ticket no está disponible" };

    private static DateTime FechaPg(DateTime fecha)
    {
        var utc = fecha.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(fecha, DateTimeKind.Utc) : fecha.ToUniversalTime();
        return DateTime.SpecifyKind(new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc), DateTimeKind.Unspecified);
    }
}

public record ValidarTicketRequest(string QrData);
