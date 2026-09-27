namespace TicketsCombustible.Api.Contracts;

public record CrearSolicitudRequest(long EmpleadoId, long VehiculoId, long DepartamentoId, long TipoCombustibleId, decimal CantidadSolicitadaGalones, DateTime? FechaVencimiento, long? UsuarioCreadorId, string TipoSolicitud = "MANUAL", string? Motivo = null);
public record AprobarSolicitudRequest(decimal CantidadAutorizadaGalones, DateTime FechaVencimiento, long UsuarioAprobadorId);
public record CrearTicketRequest(long SolicitudId, long? UsuarioEmisorId);
public sealed record CrearCierreDiarioRequest(long EstacionId, DateOnly Fecha, List<InventarioFisicoTanqueRequest> InventariosFisicos, string? Observaciones = null);
public sealed record EnviarTicketRequest(string Canal, Guid IdempotencyKey);
public sealed record ReconciliarEnvioTicketRequest(string Estado);
public sealed record InventarioFisicoTanqueRequest(long TanqueId, decimal? InventarioFisicoGalones);
public sealed record AnularTicketRequest
{
    [System.ComponentModel.DataAnnotations.StringLength(500)]
    public string? Motivo { get; init; }
}
public record RegistrarDespachoRequest(string TicketId, decimal GalonesServidos, string? Observaciones, long? TanqueId = null, long? EstacionId = null, long? OperadorId = null, decimal? OdometroKm = null, bool? IdentidadConfirmada = null);
