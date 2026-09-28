using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using TicketsCombustible.Api;
using Xunit;

namespace TicketsCombustible.Api.Tests;

public sealed class SecurityConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("short")]
    [InlineData("REEMPLAZA_CON_UN_SECRETO_ALEATORIO_DE_32_CARACTERES")]
    [InlineData("default")]
    [InlineData("changeme")]
    public void Secret_critico_ausente_debil_o_placeholder_se_rechaza(string? value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => SecurityConfiguration.RequireStrongSecret(value, "Jwt:Key"));
        Assert.Contains("Jwt:Key", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value ?? "<null>", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuracion_production_rechaza_secreto_QR_ausente_sin_revelar_valores()
    {
        var configuration = Config(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "TEST ONLY jwt signing value at least thirty two bytes",
            ["Qr:SigningSecret"] = ""
        });
        var error = Assert.Throws<InvalidOperationException>(() => SecurityConfiguration.Validate(configuration, Environment("Production")));
        Assert.Contains("Qr:SigningSecret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("TEST ONLY", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuracion_production_acepta_secretos_sinteticos_y_conexion_no_placeholder()
    {
        var configuration = Config(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "TEST ONLY jwt signing value at least thirty two bytes",
            ["Qr:SigningSecret"] = "TEST ONLY qr signing value at least thirty two bytes",
            ["ConnectionStrings:TicketsCombustible"] = "Host=qa-db.internal;Database=qa;Username=runtime;Password=TEST_ONLY_RANDOM_VALUE",
            ["ReverseProxy:KnownProxies:0"] = "127.0.0.1"
        });
        SecurityConfiguration.Validate(configuration, Environment("Production"));
    }

    [Fact]
    public void Configuracion_production_rechaza_conexion_con_placeholder()
    {
        var configuration = Config(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "TEST ONLY jwt signing value at least thirty two bytes",
            ["Qr:SigningSecret"] = "TEST ONLY qr signing value at least thirty two bytes",
            ["ConnectionStrings:TicketsCombustible"] = "Host=localhost;Database=qa;Username=postgres;Password=TU_CLAVE"
        });
        var error = Assert.Throws<InvalidOperationException>(() => SecurityConfiguration.Validate(configuration, Environment("Production")));
        Assert.Contains("ConnectionStrings__TicketsCombustible", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("TU_CLAVE", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Configuracion_production_rechaza_inicio_sin_HTTPS_ni_proxy_confiable()
    {
        var configuration = Config(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "TEST ONLY jwt signing value at least thirty two bytes",
            ["Qr:SigningSecret"] = "TEST ONLY qr signing value at least thirty two bytes",
            ["ConnectionStrings:TicketsCombustible"] = "Host=qa-db.internal;Database=qa;Username=runtime;Password=TEST_ONLY_RANDOM_VALUE"
        });
        var error = Assert.Throws<InvalidOperationException>(() => SecurityConfiguration.Validate(configuration, Environment("Production")));
        Assert.Contains("HTTPS", error.Message, StringComparison.Ordinal);
    }

    private static IConfiguration Config(IDictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IWebHostEnvironment Environment(string name) => new TestHostEnvironment { EnvironmentName = name };

    private sealed class TestHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
