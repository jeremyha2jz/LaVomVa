using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TicketsCombustible.Api.Data;

namespace TicketsCombustible.Api.Tests;

public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    public const string TestPassword = "Qa-test-password-42";
    public const string JwtSecret = "qa-jwt-signing-key-only-for-local-tests-000000";
    public const string QrSecret = "qa-qr-signing-key-only-for-local-tests-0000000";

    private readonly string connectionString;

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
            ["Logging:LogLevel:Default"] = "Warning"
        }));
    }

    public async Task ResetDatabaseAsync()
    {
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
