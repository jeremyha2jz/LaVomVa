using System.Net;
using System.Net.Mail;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;

namespace TicketsCombustible.Api.Services;

public sealed record TicketEmailMessage(string To, string Subject, string TextBody, string HtmlBody, byte[] QrPng, string IdempotencyKey);
public sealed record TicketSmsMessage(string To, string Body, string IdempotencyKey);

public interface IEmailSender
{
    string ProviderName { get; }
    Task SendAsync(TicketEmailMessage message, CancellationToken cancellationToken);
}

public interface ISmsSender
{
    string ProviderName { get; }
    Task SendAsync(TicketSmsMessage message, CancellationToken cancellationToken);
}

public sealed class DeliveryProviderException(string safeMessage, Exception? inner = null, bool outcomeUncertain = false) : Exception(safeMessage, inner)
{
    public string SafeMessage { get; } = safeMessage;
    public bool OutcomeUncertain { get; } = outcomeUncertain;
}

public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public string ProviderName => "SMTP";

    public async Task SendAsync(TicketEmailMessage content, CancellationToken cancellationToken)
    {
        var host = configuration["Smtp:Host"];
        var username = configuration["Smtp:Username"];
        var password = configuration["Smtp:Password"];
        var from = configuration["Smtp:From"];
        var port = configuration.GetValue<int?>("Smtp:Port") ?? 587;
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new DeliveryProviderException("El proveedor SMTP no está configurado.");

        try
        {
            using var message = new MailMessage(from, content.To, content.Subject, content.TextBody);
            message.Headers.Add("X-Idempotency-Key", content.IdempotencyKey);
            var html = AlternateView.CreateAlternateViewFromString(content.HtmlBody, Encoding.UTF8, MediaTypeNames.Text.Html);
            using var qr = new MemoryStream(content.QrPng, writable: false);
            var image = new LinkedResource(qr, MediaTypeNames.Image.Png) { ContentId = "ticket-qr", TransferEncoding = TransferEncoding.Base64 };
            html.LinkedResources.Add(image);
            message.AlternateViews.Add(html);
            using var client = new SmtpClient(host, port)
            {
                EnableSsl = configuration.GetValue("Smtp:EnableSsl", true),
                Credentials = new NetworkCredential(username, password),
                Timeout = configuration.GetValue("Smtp:TimeoutMilliseconds", 10000)
            };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(configuration.GetValue("Smtp:TimeoutMilliseconds", 10000)));
            await client.SendMailAsync(message, timeout.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DeliveryProviderException("Tiempo de espera agotado al conectar con SMTP; verifica el proveedor antes de reintentar.", ex, outcomeUncertain: true);
        }
        catch (SmtpException ex)
        {
            throw new DeliveryProviderException(ex.StatusCode == SmtpStatusCode.MailboxUnavailable
                ? "SMTP rechazó el destinatario." : "El proveedor SMTP rechazó el envío.", ex);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw new DeliveryProviderException("No fue posible completar el envío SMTP.", ex); }
    }
}

/// <summary>Adapter for an SMS gateway exposing a JSON POST {to, message, idempotencyKey} and bearer auth.</summary>
public sealed class HttpSmsSender(HttpClient http, IConfiguration configuration) : ISmsSender
{
    public string ProviderName => configuration["Sms:Provider"] ?? "HTTP-SMS";

    public async Task SendAsync(TicketSmsMessage message, CancellationToken cancellationToken)
    {
        var endpoint = configuration["Sms:Endpoint"];
        var apiKey = configuration["Sms:ApiKey"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(apiKey))
            throw new DeliveryProviderException("El proveedor SMS no está configurado con endpoint HTTPS y credencial.");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Add("Idempotency-Key", message.IdempotencyKey);
        request.Content = JsonContent.Create(new { to = message.To, message = message.Body, idempotencyKey = message.IdempotencyKey });
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(configuration.GetValue("Sms:TimeoutMilliseconds", 10000)));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout)
                    throw new DeliveryProviderException("Tiempo de espera agotado en el proveedor SMS; verifica el proveedor antes de reintentar.", outcomeUncertain: true);
                throw new DeliveryProviderException((int)response.StatusCode >= 500
                    ? "El proveedor SMS informó un error temporal." : "El proveedor SMS rechazó el destinatario o mensaje.");
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DeliveryProviderException("Tiempo de espera agotado en el proveedor SMS; verifica el proveedor antes de reintentar.", ex, outcomeUncertain: true);
        }
        catch (HttpRequestException ex)
        {
            throw new DeliveryProviderException("No fue posible confirmar la respuesta del proveedor SMS; verifica el proveedor antes de reintentar.", ex, outcomeUncertain: true);
        }
    }
}
