using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TicketsCombustible.Api.Common;

/// <summary>Serializa IPAddress como texto ("10.0.0.5", "::1"); System.Text.Json falla con direcciones IPv6.</summary>
public sealed class IpAddressJsonConverter : JsonConverter<IPAddress>
{
    public override IPAddress? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() is { } value ? IPAddress.Parse(value) : null;

    public override void Write(Utf8JsonWriter writer, IPAddress value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
