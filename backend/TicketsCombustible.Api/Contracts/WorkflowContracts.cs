namespace TicketsCombustible.Api.Contracts;

public record CrearSolicitudRequest(long EmpleadoId, long VehiculoId, long DepartamentoId, long TipoCombustibleId, decimal CantidadSolicitadaGalones, DateTime? FechaVencimiento, long? UsuarioCreadorId, string TipoSolicitud = "MANUAL", string? Motivo = null);
public record AprobarSolicitudRequest(decimal CantidadAutorizadaGalones, DateTime FechaVencimiento, long UsuarioAprobadorId);
public record CrearTicketRequest(long SolicitudId, long? UsuarioEmisorId);
public record RegistrarDespachoRequest(string TicketId, decimal GalonesServidos, string? Observaciones, long? TanqueId = null, long? EstacionId = null, long? OperadorId = null, decimal? OdometroKm = null, bool? IdentidadConfirmada = null);
