using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace TicketsCombustible.Api;

public static class SecurityConfiguration
{
    private static readonly string[] UnsafeValues = ["changeme", "change-me", "default", "secret", "password", "123456", "replace-me", "reemplaza", "tu_clave"];

    public static void Validate(IConfiguration configuration, IWebHostEnvironment environment)
    {
        RequireStrongSecret(configuration["Jwt:Key"], "Jwt:Key");
        RequireStrongSecret(configuration["Qr:SigningSecret"], "Qr:SigningSecret");

        if (!environment.IsDevelopment())
        {
            var connection = configuration.GetConnectionString("TicketsCombustible");
            if (string.IsNullOrWhiteSpace(connection))
                throw new InvalidOperationException("ConnectionStrings__TicketsCombustible es obligatoria y no puede usar valores de ejemplo.");
            NpgsqlConnectionStringBuilder database;
            try { database = new NpgsqlConnectionStringBuilder(connection); }
            catch (ArgumentException) { throw new InvalidOperationException("ConnectionStrings__TicketsCombustible debe ser una cadena PostgreSQL válida."); }
            if (string.IsNullOrWhiteSpace(database.Password) || ContainsUnsafeMarker(database.Password))
                throw new InvalidOperationException("ConnectionStrings__TicketsCombustible requiere una contraseña privada sin placeholders.");

            var proxies = configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
            if (proxies.Any(proxy => !System.Net.IPAddress.TryParse(proxy, out _)))
                throw new InvalidOperationException("ReverseProxy__KnownProxies debe contener direcciones IP explícitas y confiables.");
            var httpsUrl = configuration["Kestrel:Endpoints:Https:Url"];
            var certificatePath = configuration["Kestrel:Endpoints:Https:Certificate:Path"];
            var kestrelTls = Uri.TryCreate(httpsUrl, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && !string.IsNullOrWhiteSpace(certificatePath);
            if (proxies.Length == 0 && !kestrelTls)
                throw new InvalidOperationException("Configura Kestrel con un endpoint HTTPS y certificado externo, o ReverseProxy:KnownProxies con IPs confiables, antes de iniciar en Production.");
        }
    }

    public static void RequireStrongSecret(string? value, string settingName)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) < 32 || ContainsUnsafeMarker(value))
            throw new InvalidOperationException($"{settingName} requiere un secreto privado aleatorio de al menos 32 bytes; configura la variable correspondiente sin usar placeholders.");
    }

    private static bool ContainsUnsafeMarker(string value) =>
        UnsafeValues.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
