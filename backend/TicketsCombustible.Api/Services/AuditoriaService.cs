using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Models;

namespace TicketsCombustible.Api.Services;

public interface IAuditoriaService
{
    Task RegistrarAsync(
        string accion,
        string entidad,
        string? entidadId,
        string resultado,
        object? datosAnteriores = null,
        object? datosNuevos = null,
        string? detalle = null,
        long? usuarioId = null,
        CancellationToken cancellationToken = default);
}

public sealed class AuditoriaService(TicketsCombustibleDbContext db, IHttpContextAccessor httpContextAccessor) : IAuditoriaService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RegistrarAsync(
        string accion,
        string entidad,
        string? entidadId,
        string resultado,
        object? datosAnteriores = null,
        object? datosNuevos = null,
        string? detalle = null,
        long? usuarioId = null,
        CancellationToken cancellationToken = default)
    {
        var context = httpContextAccessor.HttpContext;
        if (usuarioId is null && long.TryParse(context?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId))
            usuarioId = currentUserId;

        var row = new RegistroAuditoria
        {
            UsuarioId = usuarioId,
            Accion = accion,
            Entidad = entidad,
            EntidadId = entidadId,
            DatosAnteriores = SafeJson(datosAnteriores),
            DatosNuevos = SafeJson(datosNuevos),
            DireccionIp = context?.Connection.RemoteIpAddress,
            FechaHora = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
            Resultado = resultado == "EXITO" ? "EXITO" : "FALLO",
            Detalle = detalle
        };
        db.Auditoria.Add(row);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static JsonDocument? SafeJson(object? value)
    {
        if (value is null) return null;
        var node = JsonSerializer.SerializeToNode(value, JsonOptions);
        RemoveSecrets(node);
        return node is null ? null : JsonDocument.Parse(node.ToJsonString(JsonOptions));
    }

    private static void RemoveSecrets(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj.ToArray())
            {
                if (IsSecret(pair.Key)) obj.Remove(pair.Key);
                else RemoveSecrets(pair.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array) RemoveSecrets(child);
        }
    }

    private static bool IsSecret(string key)
    {
        var normalized = key.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        return normalized.Contains("password", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("contrasena", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("token", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("hash", StringComparison.OrdinalIgnoreCase);
    }
}
