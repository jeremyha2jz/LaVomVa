using System.Net;
using System.Text.Json;
using TicketsCombustible.Api.Common;
using Xunit;

namespace TicketsCombustible.Api.Tests;

public sealed class IpAddressJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new IpAddressJsonConverter() } };

    [Theory]
    [InlineData("::1")]
    [InlineData("10.0.0.5")]
    public void Serializa_direcciones_IPv4_e_IPv6_como_texto(string ip)
    {
        var json = JsonSerializer.Serialize(new { direccionIp = IPAddress.Parse(ip) }, Options);
        Assert.Equal($"{{\"direccionIp\":\"{ip}\"}}", json);
        Assert.Equal(IPAddress.Parse(ip), JsonSerializer.Deserialize<IPAddress>($"\"{ip}\"", Options));
    }
}
