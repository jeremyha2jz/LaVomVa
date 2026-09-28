using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace TicketsCombustible.Api.Models;

public class Rol { [Key, Column("id_role")] public long Id { get; set; } [Column("nombre")] public string Nombre { get; set; } = null!; [Column("descripcion")] public string? Descripcion { get; set; } [Column("activo")] public bool Activo { get; set; } }
public class Usuario { [Key, Column("id_usuario")] public long Id { get; set; } [Column("nombre_usuario")] public string NombreUsuario { get; set; } = null!; [Column("correo")] public string Correo { get; set; } = null!; [Column("password_hash")] public string PasswordHash { get; set; } = null!; [Column("nombre_completo")] public string NombreCompleto { get; set; } = null!; [Column("telefono")] public string? Telefono { get; set; } [Column("activo")] public bool Activo { get; set; } = true; }
public class UsuarioRol { [Column("id_usuario")] public long UsuarioId { get; set; } [Column("id_role")] public long RolId { get; set; } }
public sealed class SesionUsuario
{
    [Key, Column("id_sesion")] public Guid Id { get; set; }
    [Column("id_familia")] public Guid FamiliaId { get; set; }
    [Column("id_usuario")] public long UsuarioId { get; set; }
    [Column("hash_refresh_token")] public string HashRefreshToken { get; set; } = null!;
    [Column("creado_en")] public DateTimeOffset CreadoEn { get; set; }
    [Column("expira_en")] public DateTimeOffset ExpiraEn { get; set; }
    [Column("revocado_en")] public DateTimeOffset? RevocadoEn { get; set; }
    [Column("reemplazado_por_id")] public Guid? ReemplazadoPorId { get; set; }
    [Column("ultimo_uso_en")] public DateTimeOffset? UltimoUsoEn { get; set; }
}
public sealed class Notificacion
{
    [Key, Column("id_notificacion")] public long Id { get; set; }
    [Column("id_usuario")] public long? UsuarioId { get; set; }
    [Column("tipo")] public string Tipo { get; set; } = null!;
    [Column("titulo")] public string Titulo { get; set; } = null!;
    [Column("mensaje")] public string Mensaje { get; set; } = null!;
    [Column("canal")] public string Canal { get; set; } = "SISTEMA";
    [Column("estado")] public string Estado { get; set; } = "PENDIENTE";
    [Column("fecha_creacion")] public DateTime FechaCreacion { get; set; }
    [Column("fecha_envio")] public DateTime? FechaEnvio { get; set; }
    [Column("referencia_tipo")] public string? ReferenciaTipo { get; set; }
    [Column("referencia_id")] public string? ReferenciaId { get; set; }
    [Column("severidad")] public string Severidad { get; set; } = "INFO";
    [Column("fecha_lectura")] public DateTime? FechaLectura { get; set; }
    [Column("clave_deduplicacion")] public string? ClaveDeduplicacion { get; set; }
    [Column("metadata", TypeName = "jsonb")] public JsonDocument Metadata { get; set; } = JsonDocument.Parse("{}");
}
public class MovimientoInventario { [Key, Column("id_movimiento")] public long Id { get; set; } [Column("id_tanque")] public long TanqueId { get; set; } [Column("tipo_movimiento")] public string TipoMovimiento { get; set; } = null!; [Column("cantidad_galones")] public decimal CantidadGalones { get; set; } [Column("existencia_anterior")] public decimal? ExistenciaAnterior { get; set; } [Column("existencia_nueva")] public decimal? ExistenciaNueva { get; set; } [Column("referencia_tipo")] public string? ReferenciaTipo { get; set; } [Column("referencia_id")] public string? ReferenciaId { get; set; } [Column("motivo")] public string? Motivo { get; set; } [Column("id_usuario")] public long? UsuarioId { get; set; } [Column("fecha_hora")] public DateTime FechaHora { get; set; } }
public class Proveedor { [Key, Column("id_proveedor")] public long Id { get; set; } [Column("rnc")] public string? Rnc { get; set; } [Column("nombre")] public string Nombre { get; set; } = null!; [Column("telefono")] public string? Telefono { get; set; } [Column("correo")] public string? Correo { get; set; } [Column("activo")] public bool Activo { get; set; } = true; }
public class RecepcionCombustible { [Key, Column("id_recepcion")] public long Id { get; set; } [Column("id_proveedor")] public long ProveedorId { get; set; } [Column("numero_factura")] public string NumeroFactura { get; set; } = null!; [Column("fecha_recepcion")] public DateTime FechaRecepcion { get; set; } [Column("id_usuario_receptor")] public long UsuarioReceptorId { get; set; } [Column("observaciones")] public string? Observaciones { get; set; } }
public class DetalleRecepcion { [Key, Column("id_detalle")] public long Id { get; set; } [Column("id_recepcion")] public long RecepcionId { get; set; } [Column("id_tanque")] public long TanqueId { get; set; } [Column("volumen_recibido_galones")] public decimal VolumenRecibidoGalones { get; set; } [Column("costo_unitario")] public decimal? CostoUnitario { get; set; } }

public class CierreDiario
{
    [Key, Column("id_cierre")] public long Id { get; set; }
    [Column("id_estacion")] public long EstacionId { get; set; }
    [Column("fecha")] public DateOnly Fecha { get; set; }
    [Column("inventario_inicial_galones")] public decimal InventarioInicialGalones { get; set; }
    [Column("volumen_recibido_galones")] public decimal VolumenRecibidoGalones { get; set; }
    [Column("volumen_despachado_galones")] public decimal VolumenDespachadoGalones { get; set; }
    [Column("mermas_galones")] public decimal MermasGalones { get; set; }
    [Column("ajustes_galones")] public decimal AjustesGalones { get; set; }
    [Column("inventario_final_galones")] public decimal InventarioFinalGalones { get; set; }
    [Column("diferencia_galones")] public decimal DiferenciaGalones { get; set; }
    [Column("id_usuario_cierre")] public long UsuarioCierreId { get; set; }
    [Column("ruta_acta_pdf")] public string? RutaActaPdf { get; set; }
    [Column("observaciones")] public string? Observaciones { get; set; }
    [Column("cerrado_en")] public DateTime CerradoEn { get; set; }
    [Column("inventario_fisico_galones")] public decimal InventarioFisicoGalones { get; set; }
    [Column("cantidad_despachos")] public int CantidadDespachos { get; set; }
    [Column("estado")] public string Estado { get; set; } = "CERRADO";
    [Column("detalle_tanques", TypeName = "jsonb")] public JsonDocument DetalleTanques { get; set; } = JsonDocument.Parse("[]");
}

public class Departamento { [Key, Column("id_departamento")] public long Id { get; set; } [Column("codigo")] public string? Codigo { get; set; } [Column("nombre")] public string Nombre { get; set; } = null!; [Column("descripcion")] public string? Descripcion { get; set; } [Column("activo")] public bool Activo { get; set; } = true; }
public class Empleado { [Key, Column("id_empleado")] public long Id { get; set; } [Column("codigo_empleado")] public string CodigoEmpleado { get; set; } = null!; [Column("nombre_completo")] public string NombreCompleto { get; set; } = null!; [Column("cedula")] public string Cedula { get; set; } = null!; [Column("id_departamento")] public long DepartamentoId { get; set; } [Column("cargo")] public string? Cargo { get; set; } [Column("correo")] public string? Correo { get; set; } [Column("telefono_movil")] public string? TelefonoMovil { get; set; } [Column("activo")] public bool Activo { get; set; } = true; }
public class Vehiculo { [Key, Column("id_vehiculo")] public long Id { get; set; } [Column("placa")] public string Placa { get; set; } = null!; [Column("ficha")] public string Ficha { get; set; } = null!; [Column("marca")] public string Marca { get; set; } = null!; [Column("modelo")] public string Modelo { get; set; } = null!; [Column("anio")] public short? Anio { get; set; } [Column("tipo")] public string? Tipo { get; set; } [Column("id_departamento")] public long DepartamentoId { get; set; } [Column("capacidad_tanque_galones")] public decimal? CapacidadTanqueGalones { get; set; } [Column("odometro_km")] public decimal OdometroKm { get; set; } [Column("activo")] public bool Activo { get; set; } = true; }
public class TipoCombustible { [Key, Column("id_tipo_combustible")] public long Id { get; set; } [Column("nombre")] public string Nombre { get; set; } = null!; [Column("activo")] public bool Activo { get; set; } }
public class Tanque { [Key, Column("id_tanque")] public long Id { get; set; } [Column("codigo")] public string Codigo { get; set; } = null!; [Column("nombre")] public string? Nombre { get; set; } [Column("id_estacion")] public long EstacionId { get; set; } [Column("id_tipo_combustible")] public long TipoCombustibleId { get; set; } [Column("capacidad_galones")] public decimal CapacidadGalones { get; set; } [Column("existencia_actual_galones")] public decimal ExistenciaActualGalones { get; set; } [Column("nivel_critico_galones")] public decimal NivelCriticoGalones { get; set; } [Column("activo")] public bool Activo { get; set; } [Column("notificacion_bajo_activa")] public bool NotificacionBajoActiva { get; set; } [Column("numero_episodio_bajo")] public long NumeroEpisodioBajo { get; set; } }
public class Estacion { [Key, Column("id_estacion")] public long Id { get; set; } [Column("nombre")] public string Nombre { get; set; } = null!; [Column("ubicacion")] public string? Ubicacion { get; set; } [Column("activo")] public bool Activo { get; set; } = true; }
public enum EstadoSolicitud { PENDIENTE, APROBADA, RECHAZADA, CANCELADA }
public enum EstadoTicket { CREADO, ENVIADO, PENDIENTE, PROXIMO_A_VENCER, VENCIDO, CONSUMIDO, ANULADO }
public class SolicitudCombustible { [Key, Column("id_solicitud")] public long Id { get; set; } [Column("id_empleado")] public long EmpleadoId { get; set; } [Column("id_vehiculo")] public long VehiculoId { get; set; } [Column("id_departamento")] public long DepartamentoId { get; set; } [Column("id_tipo_combustible")] public long TipoCombustibleId { get; set; } [Column("cantidad_solicitada_galones")] public decimal CantidadSolicitadaGalones { get; set; } [Column("cantidad_autorizada_galones")] public decimal? CantidadAutorizadaGalones { get; set; } [Column("fecha_solicitud")] public DateTime FechaSolicitud { get; set; } [Column("fecha_vencimiento")] public DateTime? FechaVencimiento { get; set; } [Column("tipo_solicitud")] public string TipoSolicitud { get; set; } = "MANUAL"; [Column("frecuencia")] public string? Frecuencia { get; set; } [Column("motivo")] public string? Motivo { get; set; } [Column("estado")] public EstadoSolicitud Estado { get; set; } [Column("id_usuario_creador")] public long? UsuarioCreadorId { get; set; } [Column("id_usuario_aprobador")] public long? UsuarioAprobadorId { get; set; } [Column("fecha_aprobacion")] public DateTime? FechaAprobacion { get; set; } }

public sealed class ProgramacionSolicitud
{
    [Key, Column("id_programacion")] public long Id { get; set; }
    [Column("tipo_solicitud")] public string TipoSolicitud { get; set; } = null!;
    [Column("id_empleado")] public long EmpleadoId { get; set; }
    [Column("id_vehiculo")] public long VehiculoId { get; set; }
    [Column("id_departamento")] public long DepartamentoId { get; set; }
    [Column("id_tipo_combustible")] public long TipoCombustibleId { get; set; }
    [Column("cantidad_solicitada_galones")] public decimal CantidadSolicitadaGalones { get; set; }
    [Column("fecha_inicial")] public DateTime FechaInicial { get; set; }
    [Column("fecha_final")] public DateTime? FechaFinal { get; set; }
    [Column("frecuencia")] public string? Frecuencia { get; set; }
    [Column("proxima_ejecucion")] public DateTime? ProximaEjecucion { get; set; }
    [Column("ultima_ejecucion")] public DateTime? UltimaEjecucion { get; set; }
    [Column("activa")] public bool Activa { get; set; }
    [Column("id_usuario_creador")] public long UsuarioCreadorId { get; set; }
    [Column("creado_en")] public DateTime CreadoEn { get; set; }
    [Column("actualizado_en")] public DateTime ActualizadoEn { get; set; }
}

public sealed class EjecucionProgramada
{
    [Key, Column("id_ejecucion")] public long Id { get; set; }
    [Column("id_programacion")] public long ProgramacionId { get; set; }
    [Column("fecha_programada")] public DateTime FechaProgramada { get; set; }
    [Column("ejecutada_en")] public DateTime EjecutadaEn { get; set; }
    [Column("estado")] public string Estado { get; set; } = null!;
    [Column("id_solicitud_generada")] public long? SolicitudGeneradaId { get; set; }
    [Column("detalle_error")] public string? DetalleError { get; set; }
}
public class Ticket { [Key, Column("id_ticket")] public Guid Id { get; set; } [Column("id_solicitud")] public long SolicitudId { get; set; } [Column("numero_secuencial")] public string NumeroSecuencial { get; set; } = null!; [Column("id_empleado")] public long EmpleadoId { get; set; } [Column("id_vehiculo")] public long VehiculoId { get; set; } [Column("id_departamento")] public long DepartamentoId { get; set; } [Column("id_tipo_combustible")] public long TipoCombustibleId { get; set; } [Column("cantidad_autorizada_galones")] public decimal CantidadAutorizadaGalones { get; set; } [Column("fecha_creacion")] public DateTime FechaCreacion { get; set; } [Column("fecha_vencimiento")] public DateTime FechaVencimiento { get; set; } [Column("estado")] public EstadoTicket Estado { get; set; } [Column("qr_hash")] public string QrHash { get; set; } = null!; [Column("qr_token")] public string QrToken { get; set; } = null!; [Column("anulado_en")] public DateTime? AnuladoEn { get; set; } [Column("motivo_anulacion")] public string? MotivoAnulacion { get; set; } [Column("id_usuario_anulacion")] public long? UsuarioAnulacionId { get; set; } }
public class EnvioTicket
{
    [Key, Column("id_envio")] public long Id { get; set; }
    [Column("id_ticket")] public Guid TicketId { get; set; }
    [Column("canal")] public string Canal { get; set; } = null!;
    [Column("destino")] public string Destino { get; set; } = null!;
    [Column("estado_envio")] public string EstadoEnvio { get; set; } = "PENDIENTE";
    [Column("detalle_error")] public string? DetalleError { get; set; }
    [Column("fecha_envio")] public DateTime? FechaEnvio { get; set; }
    [Column("intento")] public int Intento { get; set; }
    [Column("lote_id")] public Guid LoteId { get; set; }
    [Column("proveedor")] public string? Proveedor { get; set; }
    [Column("resultado")] public string? Resultado { get; set; }
    [Column("idempotency_key")] public Guid IdempotencyKey { get; set; }
    [Column("solicitado_en")] public DateTime SolicitadoEn { get; set; }
}
public class Despacho { [Key, Column("id_despacho")] public long Id { get; set; } [Column("id_ticket")] public Guid TicketId { get; set; } [Column("id_tanque")] public long TanqueId { get; set; } [Column("id_estacion")] public long EstacionId { get; set; } [Column("id_operador")] public long OperadorId { get; set; } [Column("fecha_hora")] public DateTime FechaHora { get; set; } [Column("galones_servidos")] public decimal GalonesServidos { get; set; } [Column("odometro_km")] public decimal? OdometroKm { get; set; } [Column("identidad_confirmada")] public bool IdentidadConfirmada { get; set; } [Column("observaciones")] public string? Observaciones { get; set; } }

public class RegistroAuditoria
{
    [Key, Column("id_auditoria")] public long Id { get; set; }
    [Column("id_usuario")] public long? UsuarioId { get; set; }
    [Column("accion")] public string Accion { get; set; } = null!;
    [Column("entidad")] public string Entidad { get; set; } = null!;
    [Column("entidad_id")] public string? EntidadId { get; set; }
    [Column("datos_anteriores", TypeName = "jsonb")] public JsonDocument? DatosAnteriores { get; set; }
    [Column("datos_nuevos", TypeName = "jsonb")] public JsonDocument? DatosNuevos { get; set; }
    [Column("direccion_ip", TypeName = "inet")] public System.Net.IPAddress? DireccionIp { get; set; }
    [Column("fecha_hora")] public DateTime FechaHora { get; set; }
    [Column("resultado")] public string Resultado { get; set; } = "EXITO";
    [Column("detalle")] public string? Detalle { get; set; }
}
