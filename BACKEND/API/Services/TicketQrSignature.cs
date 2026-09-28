using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TicketsCombustible.Api.Models;

namespace TicketsCombustible.Api.Services;

public static class TicketQrSignature
{
    public static string Sign(Ticket ticket, string secret)
    {
        var payload = string.Join('|', "v2", ticket.Id.ToString("D"),
            ticket.SolicitudId.ToString(CultureInfo.InvariantCulture),
            ticket.EmpleadoId.ToString(CultureInfo.InvariantCulture),
            ticket.VehiculoId.ToString(CultureInfo.InvariantCulture),
            ticket.DepartamentoId.ToString(CultureInfo.InvariantCulture),
            ticket.TipoCombustibleId.ToString(CultureInfo.InvariantCulture),
            ticket.CantidadAutorizadaGalones.ToString("G29", CultureInfo.InvariantCulture),
            (ticket.FechaCreacion.Ticks / 10).ToString(CultureInfo.InvariantCulture),
            (ticket.FechaVencimiento.Ticks / 10).ToString(CultureInfo.InvariantCulture),
            ticket.QrToken);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static bool Verify(Ticket ticket, string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32 || secret.StartsWith("REEMPLAZA_")) return false;
        var expected = Encoding.ASCII.GetBytes(Sign(ticket, secret));
        var stored = Encoding.ASCII.GetBytes(ticket.QrHash);
        return stored.Length == expected.Length && CryptographicOperations.FixedTimeEquals(stored, expected);
    }
}
