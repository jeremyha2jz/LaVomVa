using System.Net.Mail;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using QRCoder;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;

namespace TicketsCombustible.Api.Services;

public sealed record DeliveryChannelResult(string Canal, string Estado, string DestinoEnmascarado, string? Error);
public sealed record TicketDeliveryOutcome(Guid TicketId, string NumeroTicket, string EstadoTicket, bool DuplicadoIdempotente, IReadOnlyList<DeliveryChannelResult> Envios);
public sealed class TicketDeliveryConflict(string message) : Exception(message);
public sealed class TicketDeliveryNotFound(string message) : Exception(message);
public sealed class TicketDeliveryInvalid(string message) : Exception(message);

/// <summary>
/// Reserves delivery rows in a short transaction, commits before contacting a provider,
/// then persists the provider outcome. Replaying an idempotency key never calls a provider twice.
/// </summary>
public sealed class TicketDeliveryService(
    TicketsCombustibleDbContext db,
    IEmailSender emailSender,
    ISmsSender smsSender,
    IAuditoriaService auditoria,
    TicketLifecycleService lifecycle,
    IConfiguration configuration,
    TimeProvider timeProvider,
    NotificacionService notifications)
{
    public async Task<TicketDeliveryOutcome> SendAsync(Guid ticketId, string requestedChannel, Guid idempotencyKey, bool retryOnlyFailed, long actorId, string? signingSecret, CancellationToken cancellationToken)
    {
        var channels = ParseChannels(requestedChannel);
        if (idempotencyKey == Guid.Empty) throw new TicketDeliveryInvalid("Debe enviar una clave de idempotencia válida.");
        if (!TicketQrSignature.Verify(await db.Tickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ticketId, cancellationToken)
                ?? throw new TicketDeliveryNotFound("Ticket no encontrado."), signingSecret))
            throw new TicketDeliveryConflict("La firma QR del ticket no es válida; no se re-firmará automáticamente.");

        List<EnvioTicket> reserved;
        TicketDeliveryData deliveryData;
        Guid batchId;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var ticket = await db.Tickets.FromSqlInterpolated($"SELECT * FROM tickets WHERE id_ticket = {ticketId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
                ?? throw new TicketDeliveryNotFound("Ticket no encontrado.");

            var replay = await db.EnviosTicket.AsNoTracking().Where(x => x.TicketId == ticketId && x.IdempotencyKey == idempotencyKey).ToListAsync(cancellationToken);
            if (replay.Count > 0)
            {
                if (!replay.Select(x => x.Canal).ToHashSet(StringComparer.Ordinal).SetEquals(channels))
                    throw new TicketDeliveryConflict("La clave de idempotencia ya se usó con otros canales.");
                await transaction.CommitAsync(cancellationToken);
                return await BuildOutcomeAsync(ticket, replay, true, cancellationToken);
            }

            var currentState = lifecycle.EstadoActual(ticket);
            if (currentState is EstadoTicket.ANULADO or EstadoTicket.CONSUMIDO or EstadoTicket.VENCIDO)
                throw new TicketDeliveryConflict($"No se puede enviar un ticket en estado {currentState}.");
            if (!lifecycle.PuedeTransicionar(currentState, EstadoTicket.PENDIENTE))
                throw new TicketDeliveryConflict($"No se puede iniciar un envío desde el estado {currentState}.");
            if (!TicketQrSignature.Verify(ticket, signingSecret))
                throw new TicketDeliveryConflict("La firma QR del ticket no es válida; no se re-firmará automáticamente.");

            deliveryData = await LoadDeliveryDataAsync(ticket, cancellationToken);
            var latest = await db.EnviosTicket.Where(x => x.TicketId == ticketId)
                .GroupBy(x => x.Canal)
                .Select(group => group.OrderByDescending(x => x.Intento).First())
                .ToListAsync(cancellationToken);
            var latestByChannel = latest.ToDictionary(x => x.Canal, StringComparer.Ordinal);
            if (retryOnlyFailed)
            {
                channels = channels.Where(channel => latestByChannel.TryGetValue(channel, out var row) && row.EstadoEnvio == "FALLIDO").ToList();
                if (channels.Count == 0) throw new TicketDeliveryConflict("Solo se pueden reintentar canales cuyo último intento falló.");
            }
            else
            {
                foreach (var channel in channels)
                {
                    if (!latestByChannel.TryGetValue(channel, out var prior)) continue;
                    if (prior.EstadoEnvio == "PENDIENTE") throw new TicketDeliveryConflict($"Ya hay un envío {channel} pendiente.");
                    if (prior.EstadoEnvio == "FALLIDO") throw new TicketDeliveryConflict($"El último envío {channel} falló; usa la opción de reintento.");
                    if (prior.EstadoEnvio == "ENVIADO") throw new TicketDeliveryConflict($"El canal {channel} ya se envió correctamente.");
                }
            }
            if (channels.Count == 0) throw new TicketDeliveryConflict("No hay canales que enviar.");

            batchId = Guid.NewGuid();
            reserved = [];
            foreach (var channel in channels)
            {
                var destination = channel == "CORREO" ? deliveryData.Email ?? "" : NormalizePhone(deliveryData.Phone) ?? deliveryData.Phone ?? "";
                var valid = channel == "CORREO" ? IsValidEmail(destination) : IsValidPhone(destination);
                var attempt = latestByChannel.TryGetValue(channel, out var previous) ? previous.Intento + 1 : 1;
                reserved.Add(new EnvioTicket
                {
                    TicketId = ticketId, Canal = channel, Destino = destination,
                    EstadoEnvio = "PENDIENTE", Intento = attempt, LoteId = batchId,
                    IdempotencyKey = idempotencyKey, SolicitadoEn = PgUtc(timeProvider.GetUtcNow().UtcDateTime),
                    Proveedor = channel == "CORREO" ? emailSender.ProviderName : smsSender.ProviderName,
                    Resultado = valid ? "PENDIENTE_CONFIRMACION" : channel == "CORREO" ? "DESTINATARIO_INVALIDO" : "TELEFONO_INVALIDO"
                });
            }
            db.EnviosTicket.AddRange(reserved);
            await db.SaveChangesAsync(cancellationToken); // A pending row exists before the lifecycle transition trigger runs.
            ticket.Estado = EstadoTicket.PENDIENTE;
            await db.SaveChangesAsync(cancellationToken);
            await auditoria.RegistrarAsync(retryOnlyFailed ? "TICKET_REENVIADO" : "TICKET_ENVIO_SOLICITADO", "TICKET", ticket.NumeroSecuencial,
                "EXITO", datosNuevos: new { loteId = batchId, canales = reserved.Select(x => x.Canal), destinos = reserved.Select(x => MaskDestination(x.Destino)) }, cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var outcomeResults = new List<(EnvioTicket Row, string State, string? SafeError)>();
        foreach (var attempt in reserved)
        {
            var channel = attempt.Canal;
            if (attempt.Resultado is "DESTINATARIO_INVALIDO" or "TELEFONO_INVALIDO")
            {
                outcomeResults.Add((attempt, "FALLIDO", channel == "CORREO" ? "La dirección de correo del empleado no es válida." : "El teléfono del empleado no tiene formato E.164 válido."));
                continue;
            }
            try
            {
                if (channel == "CORREO")
                    await emailSender.SendAsync(BuildEmail(deliveryData, attempt), cancellationToken);
                else
                    await smsSender.SendAsync(BuildSms(deliveryData, attempt), cancellationToken);
                outcomeResults.Add((attempt, "ENVIADO", null));
            }
            catch (Exception ex)
            {
                var providerError = ex as DeliveryProviderException;
                var uncertain = providerError?.OutcomeUncertain == true || ex is OperationCanceledException;
                var safeMessage = providerError?.SafeMessage ?? (ex is OperationCanceledException
                    ? "La solicitud se interrumpió durante el envío; verifica el proveedor antes de reintentar."
                    : "Falló el proveedor de entrega.");
                outcomeResults.Add((attempt, uncertain ? "PENDIENTE" : "FALLIDO", SanitizeError(safeMessage, deliveryData, signingSecret)));
            }
        }

        await using (var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None))
        {
            var ticket = await db.Tickets.FromSqlInterpolated($"SELECT * FROM tickets WHERE id_ticket = {ticketId} FOR UPDATE").SingleAsync(CancellationToken.None);
            foreach (var result in outcomeResults)
            {
                var row = await db.EnviosTicket.SingleAsync(x => x.Id == result.Row.Id, CancellationToken.None);
                row.EstadoEnvio = result.State;
                row.DetalleError = result.SafeError;
                row.Resultado = result.State switch
                {
                    "ENVIADO" => "ACEPTADO_POR_PROVEEDOR",
                    "PENDIENTE" => "RESULTADO_INCIERTO",
                    _ => result.Row.Resultado ?? "ERROR_PROVEEDOR"
                };
                row.FechaEnvio = PgUtc(timeProvider.GetUtcNow().UtcDateTime);
            }
            await db.SaveChangesAsync(CancellationToken.None);
            var allLatest = await db.EnviosTicket.Where(x => x.TicketId == ticketId)
                .GroupBy(x => x.Canal).Select(group => group.OrderByDescending(x => x.Intento).First().EstadoEnvio)
                .ToListAsync(CancellationToken.None);
            var target = allLatest.Count > 0 && allLatest.All(x => x == "ENVIADO") ? EstadoTicket.ENVIADO : EstadoTicket.PENDIENTE;
            if (ticket.Estado != target)
            {
                if (lifecycle.PuedeTransicionar(ticket.Estado, target)) ticket.Estado = target;
                await db.SaveChangesAsync(CancellationToken.None);
            }
            foreach (var result in outcomeResults)
            {
                var auditAction = result.State switch { "ENVIADO" => "TICKET_ENVIADO", "PENDIENTE" => "TICKET_ENVIO_INCIERTO", _ => "TICKET_ENVIO_FALLIDO" };
                await auditoria.RegistrarAsync(auditAction, "TICKET", ticket.NumeroSecuencial, result.State == "ENVIADO" ? "EXITO" : "FALLO",
                    datosNuevos: new { canal = result.Row.Canal, intento = result.Row.Intento, destino = MaskDestination(result.Row.Destino), loteId = batchId, estadoEnvio = result.State },
                    detalle: result.SafeError, usuarioId: actorId, cancellationToken: CancellationToken.None);
                if (result.State == "FALLIDO")
                    await notifications.CrearParaRolesAsync(["ADMINISTRADOR", "SUPERVISOR"], "FALLO_INTEGRACION",
                        "Falló la entrega de un ticket", $"El ticket {ticket.NumeroSecuencial} falló por el canal {result.Row.Canal} ({result.State}) a las {timeProvider.GetUtcNow():yyyy-MM-dd HH:mm} UTC.",
                        "AVISO", "ENVIO_TICKET", result.Row.Id.ToString(), $"envio:{result.Row.Id}:FALLO_INTEGRACION",
                        new { ticketId = ticket.Id, ticket = ticket.NumeroSecuencial, canal = result.Row.Canal, estado = result.State, momentoUtc = timeProvider.GetUtcNow() }, CancellationToken.None);
            }
            await transaction.CommitAsync(CancellationToken.None);
        }

        var completedTicket = await db.Tickets.AsNoTracking().SingleAsync(x => x.Id == ticketId, CancellationToken.None);
        var finalRows = await db.EnviosTicket.AsNoTracking().Where(x => x.LoteId == batchId).OrderBy(x => x.Id).ToListAsync(CancellationToken.None);
        return await BuildOutcomeAsync(completedTicket, finalRows, false, CancellationToken.None);
    }

    public Task<List<EnvioTicket>> HistoryAsync(Guid ticketId, CancellationToken cancellationToken) =>
        db.EnviosTicket.AsNoTracking().Where(x => x.TicketId == ticketId).OrderByDescending(x => x.SolicitadoEn).ThenByDescending(x => x.Id).ToListAsync(cancellationToken);

    public async Task<TicketDeliveryOutcome> ReconcileAsync(Guid ticketId, long envioId, string confirmedState, long actorId, CancellationToken cancellationToken)
    {
        var state = confirmedState.Trim().ToUpperInvariant();
        if (state is not ("ENVIADO" or "FALLIDO")) throw new TicketDeliveryInvalid("Confirma ENVIADO o FALLIDO después de revisar el resultado del proveedor.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var ticket = await db.Tickets.FromSqlInterpolated($"SELECT * FROM tickets WHERE id_ticket = {ticketId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
            ?? throw new TicketDeliveryNotFound("Ticket no encontrado.");
        var attempt = await db.EnviosTicket.FromSqlInterpolated($"SELECT * FROM envios_ticket WHERE id_envio = {envioId} AND id_ticket = {ticketId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
            ?? throw new TicketDeliveryNotFound("Intento de envío no encontrado.");
        if (attempt.EstadoEnvio != "PENDIENTE" || attempt.Resultado is not ("RESULTADO_INCIERTO" or "PENDIENTE_CONFIRMACION"))
            throw new TicketDeliveryConflict("Solo se puede reconciliar un intento pendiente de confirmación.");
        var reconciliationDelay = TimeSpan.FromMinutes(Math.Clamp(configuration.GetValue("TicketDelivery:ReconciliationDelayMinutes", 5), 1, 60));
        if (timeProvider.GetUtcNow().UtcDateTime - DateTime.SpecifyKind(attempt.SolicitadoEn, DateTimeKind.Utc) < reconciliationDelay)
            throw new TicketDeliveryConflict("El proveedor aún puede estar procesando el envío. Espera antes de reconciliarlo.");

        attempt.EstadoEnvio = state;
        attempt.Resultado = state == "ENVIADO" ? "CONFIRMADO_MANUALMENTE_ENVIADO" : "CONFIRMADO_MANUALMENTE_FALLIDO";
        if (state == "ENVIADO") attempt.DetalleError = null;
        await db.SaveChangesAsync(cancellationToken);
        var latest = await db.EnviosTicket.Where(x => x.TicketId == ticketId)
            .GroupBy(x => x.Canal).Select(group => group.OrderByDescending(x => x.Intento).First().EstadoEnvio).ToListAsync(cancellationToken);
        var target = latest.Count > 0 && latest.All(x => x == "ENVIADO") ? EstadoTicket.ENVIADO : EstadoTicket.PENDIENTE;
        if (ticket.Estado != target && lifecycle.PuedeTransicionar(ticket.Estado, target))
        {
            ticket.Estado = target;
            await db.SaveChangesAsync(cancellationToken);
        }
        await auditoria.RegistrarAsync("TICKET_ENVIO_RECONCILIADO", "TICKET", ticket.NumeroSecuencial, "EXITO",
            datosNuevos: new { intentoId = attempt.Id, canal = attempt.Canal, intento = attempt.Intento, resultadoConfirmado = state,
                destino = MaskDestination(attempt.Destino), loteId = attempt.LoteId }, usuarioId: actorId, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var rows = await db.EnviosTicket.AsNoTracking().Where(x => x.TicketId == ticketId).OrderBy(x => x.Id).ToListAsync(cancellationToken);
        return await BuildOutcomeAsync(ticket, rows, false, cancellationToken);
    }

    public async Task<byte[]?> PublicQrAsync(string token, string? signingSecret, CancellationToken cancellationToken)
    {
        var ticket = await db.Tickets.AsNoTracking().SingleOrDefaultAsync(x => x.QrToken == token, cancellationToken);
        if (ticket is null || !TicketQrSignature.Verify(ticket, signingSecret)) return null;
        var state = lifecycle.EstadoActual(ticket);
        if (state is EstadoTicket.ANULADO or EstadoTicket.CONSUMIDO or EstadoTicket.VENCIDO) return null;
        using var generator = new QRCodeGenerator();
        using var code = generator.CreateQrCode(ticket.QrToken, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(code).GetGraphic(12);
    }

    private TicketEmailMessage BuildEmail(TicketDeliveryData data, EnvioTicket attempt)
    {
        using var generator = new QRCodeGenerator();
        using var code = generator.CreateQrCode(data.Ticket.QrToken, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(code).GetGraphic(12);
        var subject = $"Ticket de combustible {data.Ticket.NumeroSecuencial}";
        var expiry = data.Ticket.FechaVencimiento.ToString("yyyy-MM-dd HH:mm 'UTC'");
        var text = $"Ticket {data.Ticket.NumeroSecuencial}\nEmpleado: {data.Employee}\nVehículo: {data.Vehicle}\nDepartamento: {data.Department}\nCombustible: {data.Fuel}\nCantidad autorizada: {data.Ticket.CantidadAutorizadaGalones:0.00} gal\nEmitido: {data.Ticket.FechaCreacion:yyyy-MM-dd HH:mm 'UTC'}\nVence: {expiry}\nPresenta el QR adjunto para validación.";
        var html = $"<h1>Ticket de combustible {WebUtility.HtmlEncode(data.Ticket.NumeroSecuencial)}</h1><p>Empleado: {WebUtility.HtmlEncode(data.Employee)}<br>Vehículo: {WebUtility.HtmlEncode(data.Vehicle)}<br>Departamento: {WebUtility.HtmlEncode(data.Department)}<br>Combustible: {WebUtility.HtmlEncode(data.Fuel)}<br>Cantidad autorizada: {data.Ticket.CantidadAutorizadaGalones:0.00} gal<br>Emitido: {data.Ticket.FechaCreacion:yyyy-MM-dd HH:mm} UTC<br>Vence: {expiry}</p><p>Presenta este QR para validar el ticket.</p><img src=\"cid:ticket-qr\" alt=\"QR del ticket\" />";
        return new TicketEmailMessage(attempt.Destino, subject, text, html, png, attempt.IdempotencyKey.ToString("N"));
    }

    private TicketSmsMessage BuildSms(TicketDeliveryData data, EnvioTicket attempt)
    {
        var baseUrl = configuration["TicketDelivery:PublicBaseUrl"]?.TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new DeliveryProviderException("No está configurada una URL pública HTTPS para el QR.");
        var url = $"{baseUrl}/api/tickets/public/qr?token={Uri.EscapeDataString(data.Ticket.QrToken)}";
        var text = $"Ticket {data.Ticket.NumeroSecuencial} · {data.Ticket.CantidadAutorizadaGalones:0.##} gal {data.Fuel}. Vence {data.Ticket.FechaVencimiento:yyyy-MM-dd HH:mm} UTC. QR: {url}";
        return new TicketSmsMessage(attempt.Destino, text, attempt.IdempotencyKey.ToString("N"));
    }

    private async Task<TicketDeliveryData> LoadDeliveryDataAsync(Ticket ticket, CancellationToken cancellationToken) => await (
        from employee in db.Empleados.AsNoTracking()
        join vehicle in db.Vehiculos.AsNoTracking() on ticket.VehiculoId equals vehicle.Id
        join department in db.Departamentos.AsNoTracking() on ticket.DepartamentoId equals department.Id
        join fuel in db.TiposCombustible.AsNoTracking() on ticket.TipoCombustibleId equals fuel.Id
        where employee.Id == ticket.EmpleadoId
        select new TicketDeliveryData(ticket, employee.NombreCompleto, vehicle.Placa, department.Nombre, fuel.Nombre, employee.Correo, employee.TelefonoMovil)
    ).SingleAsync(cancellationToken);

    private Task<TicketDeliveryOutcome> BuildOutcomeAsync(Ticket ticket, IReadOnlyCollection<EnvioTicket> rows, bool replay, CancellationToken cancellationToken)
    {
        var state = lifecycle.EstadoActual(ticket).ToString();
        return Task.FromResult(new TicketDeliveryOutcome(ticket.Id, ticket.NumeroSecuencial, state, replay,
            rows.Select(x => new DeliveryChannelResult(x.Canal, x.EstadoEnvio, MaskDestination(x.Destino), x.DetalleError)).ToArray()));
    }

    private static List<string> ParseChannels(string requested)
    {
        var channel = requested.Trim().ToUpperInvariant();
        return channel switch { "EMAIL" or "CORREO" => ["CORREO"], "SMS" => ["SMS"], "AMBOS" => ["CORREO", "SMS"], _ => throw new TicketDeliveryInvalid("Canal inválido. Use EMAIL, SMS o AMBOS.") };
    }

    private static bool IsValidEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { return new MailAddress(value.Trim()).Address == value.Trim() && value.Length <= 150; }
        catch (FormatException) { return false; }
    }

    private static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 10) digits = "1" + digits;
        if (digits.Length is < 8 or > 15) return null;
        return "+" + digits;
    }

    private static bool IsValidPhone(string? value) => value is { Length: >= 9 and <= 16 } && value[0] == '+' && value.Skip(1).All(char.IsDigit) && value[1] != '0';

    public static string MaskDestination(string value)
    {
        if (value.Contains('@'))
        {
            var parts = value.Split('@', 2);
            var name = parts[0];
            return $"{(name.Length > 0 ? name[..1] : "*")}***@{parts[1]}";
        }
        if (value.Length <= 4) return "***";
        return $"{new string('*', Math.Max(0, value.Length - 4))}{value[^4..]}";
    }

    private static string SanitizeError(string message, TicketDeliveryData data, string? secret)
    {
        var safe = message;
        foreach (var value in new[] { secret, data.Ticket.QrToken, data.Email, data.Phone }.Where(x => !string.IsNullOrWhiteSpace(x)))
            safe = safe.Replace(value!, "[redactado]", StringComparison.OrdinalIgnoreCase);
        safe = System.Text.RegularExpressions.Regex.Replace(safe, "(?i)(password|api[_-]?key|token|secret)\\s*[:=]\\s*[^ ,;]+", "$1=[redactado]");
        return safe.Length > 500 ? safe[..500] : safe;
    }

    private static DateTime PgUtc(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);
    private sealed record TicketDeliveryData(Ticket Ticket, string Employee, string Vehicle, string Department, string Fuel, string? Email, string? Phone);
}
