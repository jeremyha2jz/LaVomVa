using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using TicketsCombustible.Api.Services;
using Xunit;

namespace TicketsCombustible.Api.Tests;

public sealed class TicketDeliveryProviderTests
{
    [Fact]
    public async Task Sms_gateway_envia_solo_al_endpoint_https_configurado_y_usa_clave_idempotente()
    {
        HttpRequestMessage? observed = null;
        using var http = new HttpClient(new DelegateHandler(async (request, cancellation) =>
        {
            observed = request;
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-api-key-not-real", request.Headers.Authorization?.Parameter);
            Assert.Equal("batch-key", request.Headers.GetValues("Idempotency-Key").Single());
            var content = await request.Content!.ReadAsStringAsync(cancellation);
            Assert.Equal("+18095550123", System.Text.Json.JsonDocument.Parse(content).RootElement.GetProperty("to").GetString());
            Assert.DoesNotContain("test-api-key-not-real", content, StringComparison.Ordinal);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }));
        var config = Config(new Dictionary<string, string?>
        {
            ["Sms:Endpoint"] = "https://sms.example.test/messages",
            ["Sms:ApiKey"] = "test-api-key-not-real",
            ["Sms:Provider"] = "QA-GATEWAY"
        });
        var sender = new HttpSmsSender(http, config);
        Assert.Equal("QA-GATEWAY", sender.ProviderName);
        await sender.SendAsync(new TicketSmsMessage("+18095550123", "Ticket QA", "batch-key"), CancellationToken.None);
        Assert.Equal("https://sms.example.test/messages", observed!.RequestUri!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "El proveedor SMS informó un error temporal.", false)]
    [InlineData(HttpStatusCode.RequestTimeout, "Tiempo de espera agotado en el proveedor SMS; verifica el proveedor antes de reintentar.", true)]
    [InlineData(HttpStatusCode.BadRequest, "El proveedor SMS rechazó el destinatario o mensaje.", false)]
    public async Task Sms_gateway_traduce_fallos_http_a_errores_de_integracion_seguros(HttpStatusCode status, string expected, bool outcomeUncertain)
    {
        using var http = new HttpClient(new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(status))));
        var sender = new HttpSmsSender(http, Config(new Dictionary<string, string?>
        {
            ["Sms:Endpoint"] = "https://sms.example.test/messages", ["Sms:ApiKey"] = "not-a-real-key"
        }));
        var error = await Assert.ThrowsAsync<DeliveryProviderException>(() => sender.SendAsync(
            new TicketSmsMessage("+18095550123", "Ticket QA", "key"), CancellationToken.None));
        Assert.Equal(expected, error.SafeMessage);
        Assert.Equal(outcomeUncertain, error.OutcomeUncertain);
    }

    [Fact]
    public async Task Sms_gateway_timeout_es_cancelable_y_no_reintenta_automaticamente()
    {
        using var http = new HttpClient(new DelegateHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var sender = new HttpSmsSender(http, Config(new Dictionary<string, string?>
        {
            ["Sms:Endpoint"] = "https://sms.example.test/messages", ["Sms:ApiKey"] = "not-a-real-key", ["Sms:TimeoutMilliseconds"] = "10"
        }));
        var error = await Assert.ThrowsAsync<DeliveryProviderException>(() => sender.SendAsync(
            new TicketSmsMessage("+18095550123", "Ticket QA", "key"), CancellationToken.None));
        Assert.Contains("Tiempo de espera", error.SafeMessage);
        Assert.True(error.OutcomeUncertain);
    }

    [Fact]
    public async Task Smtp_no_intenta_conexion_si_falta_configuracion_segura()
    {
        var sender = new SmtpEmailSender(Config(new Dictionary<string, string?>()));
        var error = await Assert.ThrowsAsync<DeliveryProviderException>(() => sender.SendAsync(
            new TicketEmailMessage("user@example.test", "QA", "text", "html", [], "key"), CancellationToken.None));
        Assert.Equal("El proveedor SMTP no está configurado.", error.SafeMessage);
    }

    private static IConfiguration Config(IDictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
