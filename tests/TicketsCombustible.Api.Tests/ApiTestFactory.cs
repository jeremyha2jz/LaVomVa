using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Services;

namespace TicketsCombustible.Api.Tests;

public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    public const string TestPassword = "Qa-test-password-42";
    public const string JwtSecret = "qa-jwt-signing-key-only-for-local-tests-000000";
    public const string QrSecret = "qa-qr-signing-key-only-for-local-tests-0000000";

    private readonly string connectionString;
    public QaTimeProvider Clock { get; } = new();

    public FakeEmailSender EmailFake => Services.GetRequiredService<FakeEmailSender>();
    public FakeSmsSender SmsFake => Services.GetRequiredService<FakeSmsSender>();

    public void ResetProviders() { EmailFake.Reset(); SmsFake.Reset(); }

    public ApiTestFactory(string connectionString)
    {
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        if (parsed.Database != "lavomva_test" || parsed.Host is not ("127.0.0.1" or "localhost" or "::1"))
            throw new InvalidOperationException("QA_TEST_CONNECTION solo puede apuntar a lavomva_test en loopback local.");
        this.connectionString = connectionString;
        // Minimal hosting reads values at WebApplication.CreateBuilder, before ConfigureWebHost runs.
        Environment.SetEnvironmentVariable("ConnectionStrings__TicketsCombustible", connectionString);
        Environment.SetEnvironmentVariable("Jwt__Key", JwtSecret);
        Environment.SetEnvironmentVariable("Qr__SigningSecret", QrSecret);
        Environment.SetEnvironmentVariable("Bootstrap__Secret", "qa-bootstrap-secret-that-is-not-used-000000");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:TicketsCombustible"] = connectionString,
            ["Jwt:Key"] = JwtSecret,
            ["Qr:SigningSecret"] = QrSecret,
            ["Bootstrap:Secret"] = "qa-bootstrap-secret-that-is-not-used-000000",
            ["TicketDelivery:PublicBaseUrl"] = "https://tickets.qa.example.test",
            ["Smtp:Host"] = "smtp.qa.invalid",
            ["Smtp:Username"] = "qa-user",
            ["Smtp:Password"] = "qa-password-not-used",
            ["Smtp:From"] = "tickets@qa.example.test",
            ["Sms:Endpoint"] = "https://sms.qa.invalid/send",
            ["Sms:ApiKey"] = "qa-sms-key-not-used",
            ["Sms:Provider"] = "FAKE-SMS",
            ["Logging:LogLevel:Default"] = "Warning"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IEmailSender>();
            services.RemoveAll<ISmsSender>();
            services.AddSingleton<FakeEmailSender>();
            services.AddSingleton<IEmailSender>(provider => provider.GetRequiredService<FakeEmailSender>());
            services.AddSingleton<FakeSmsSender>();
            services.AddSingleton<ISmsSender>(provider => provider.GetRequiredService<FakeSmsSender>());
        });
    }

    public async Task ResetDatabaseAsync()
    {
        Clock.Reset();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketsCombustibleDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE auditoria, notificaciones, cierres_diarios, movimientos_inventario,
              detalle_recepciones, recepciones_combustible, envios_ticket, despachos, tickets,
              solicitudes_combustible, tanques, estaciones, proveedores, vehiculos, empleados,
              departamentos, usuarios RESTART IDENTITY CASCADE
            """);
        await db.Database.ExecuteSqlRawAsync("UPDATE configuracion_tickets SET secuencia_actual = 0, anio_secuencia = EXTRACT(YEAR FROM CURRENT_DATE)::INTEGER");
    }
}

public sealed class QaTimeProvider : TimeProvider
{
    private DateTimeOffset utcNow = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => utcNow;
    public void SetUtcNow(DateTimeOffset value) => utcNow = value.ToUniversalTime();
    public void Reset() => utcNow = DateTimeOffset.UtcNow;
}
