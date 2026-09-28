using Microsoft.EntityFrameworkCore;
using TicketsCombustible.Api.Models;

namespace TicketsCombustible.Api.Data;

public class TicketsCombustibleDbContext(DbContextOptions<TicketsCombustibleDbContext> options) : DbContext(options)
{
    public DbSet<Departamento> Departamentos => Set<Departamento>();
    public DbSet<Empleado> Empleados => Set<Empleado>();
    public DbSet<Vehiculo> Vehiculos => Set<Vehiculo>();
    public DbSet<TipoCombustible> TiposCombustible => Set<TipoCombustible>();
    public DbSet<Tanque> Tanques => Set<Tanque>();
    public DbSet<Estacion> Estaciones => Set<Estacion>();
    public DbSet<SolicitudCombustible> Solicitudes => Set<SolicitudCombustible>();
    public DbSet<ProgramacionSolicitud> ProgramacionesSolicitud => Set<ProgramacionSolicitud>();
    public DbSet<EjecucionProgramada> EjecucionesProgramadas => Set<EjecucionProgramada>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<EnvioTicket> EnviosTicket => Set<EnvioTicket>();
    public DbSet<Despacho> Despachos => Set<Despacho>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Rol> Roles => Set<Rol>();
    public DbSet<UsuarioRol> UsuarioRoles => Set<UsuarioRol>();
    public DbSet<MovimientoInventario> MovimientosInventario => Set<MovimientoInventario>();
    public DbSet<Proveedor> Proveedores => Set<Proveedor>();
    public DbSet<RecepcionCombustible> Recepciones => Set<RecepcionCombustible>();
    public DbSet<DetalleRecepcion> DetallesRecepcion => Set<DetalleRecepcion>();
    public DbSet<CierreDiario> CierresDiarios => Set<CierreDiario>();
    public DbSet<RegistroAuditoria> Auditoria => Set<RegistroAuditoria>();
    public DbSet<Notificacion> Notificaciones => Set<Notificacion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Departamento>().ToTable("departamentos").HasKey(x => x.Id);
        modelBuilder.Entity<Empleado>().ToTable("empleados").HasKey(x => x.Id);
        modelBuilder.Entity<Vehiculo>().ToTable("vehiculos").HasKey(x => x.Id);
        modelBuilder.Entity<TipoCombustible>().ToTable("tipos_combustible").HasKey(x => x.Id);
        modelBuilder.Entity<Tanque>().ToTable("tanques").HasKey(x => x.Id);
        modelBuilder.Entity<Notificacion>().ToTable("notificaciones").HasKey(x => x.Id);
        modelBuilder.Entity<Notificacion>().Property(x => x.FechaCreacion).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<Notificacion>().Property(x => x.FechaEnvio).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<Notificacion>().Property(x => x.FechaLectura).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<Estacion>().ToTable("estaciones").HasKey(x => x.Id);
        modelBuilder.Entity<SolicitudCombustible>().ToTable("solicitudes_combustible").HasKey(x => x.Id);
        modelBuilder.Entity<ProgramacionSolicitud>().ToTable("programaciones_solicitud").HasKey(x => x.Id);
        modelBuilder.Entity<ProgramacionSolicitud>().Property(x => x.CantidadSolicitadaGalones).HasPrecision(10, 2);
        modelBuilder.Entity<ProgramacionSolicitud>().Property(x => x.FechaInicial).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<ProgramacionSolicitud>().Property(x => x.FechaFinal).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<ProgramacionSolicitud>().Property(x => x.ProximaEjecucion).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<ProgramacionSolicitud>().Property(x => x.UltimaEjecucion).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<ProgramacionSolicitud>().Property(x => x.CreadoEn).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<ProgramacionSolicitud>().Property(x => x.ActualizadoEn).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<EjecucionProgramada>().ToTable("ejecuciones_programadas").HasKey(x => x.Id);
        modelBuilder.Entity<EjecucionProgramada>().Property(x => x.FechaProgramada).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<EjecucionProgramada>().Property(x => x.EjecutadaEn).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<Ticket>().ToTable("tickets").HasKey(x => x.Id);
        modelBuilder.Entity<EnvioTicket>().ToTable("envios_ticket").HasKey(x => x.Id);
        modelBuilder.Entity<EnvioTicket>().Property(x => x.SolicitadoEn).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<EnvioTicket>().Property(x => x.FechaEnvio).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<Despacho>().ToTable("despachos").HasKey(x => x.Id);
        modelBuilder.Entity<Despacho>().Property(x => x.FechaHora).HasColumnType("timestamp without time zone")
            .HasDefaultValueSql("timezone('UTC', transaction_timestamp())").ValueGeneratedOnAdd();
        modelBuilder.Entity<Usuario>().ToTable("usuarios").HasKey(x => x.Id);
        modelBuilder.Entity<Rol>().ToTable("roles").HasKey(x => x.Id);
        modelBuilder.Entity<UsuarioRol>().ToTable("usuario_roles").HasKey(x => new { x.UsuarioId, x.RolId });
        modelBuilder.Entity<MovimientoInventario>().ToTable("movimientos_inventario").HasKey(x => x.Id);
        modelBuilder.Entity<MovimientoInventario>().Property(x => x.FechaHora).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<Proveedor>().ToTable("proveedores").HasKey(x => x.Id);
        modelBuilder.Entity<RecepcionCombustible>().ToTable("recepciones_combustible").HasKey(x => x.Id);
        modelBuilder.Entity<RecepcionCombustible>().Property(x => x.FechaRecepcion).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<DetalleRecepcion>().ToTable("detalle_recepciones").HasKey(x => x.Id);
        modelBuilder.Entity<CierreDiario>().ToTable("cierres_diarios").HasKey(x => x.Id);
        modelBuilder.Entity<CierreDiario>().Property(x => x.Fecha).HasColumnType("date");
        modelBuilder.Entity<CierreDiario>().Property(x => x.CerradoEn).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<CierreDiario>().Property(x => x.DetalleTanques).HasColumnType("jsonb");
        modelBuilder.Entity<RegistroAuditoria>().ToTable("auditoria").HasKey(x => x.Id);
        modelBuilder.Entity<RegistroAuditoria>().Property(x => x.FechaHora).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<SolicitudCombustible>().Property(x => x.Estado).HasConversion<string>();
        modelBuilder.Entity<SolicitudCombustible>().Property(x => x.FechaSolicitud).HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
        modelBuilder.Entity<SolicitudCombustible>().Property(x => x.FechaSolicitud).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<SolicitudCombustible>().Property(x => x.FechaVencimiento).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<SolicitudCombustible>().Property(x => x.FechaAprobacion).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<Ticket>().Property(x => x.Estado).HasConversion<string>();
        modelBuilder.Entity<Ticket>().Property(x => x.FechaVencimiento).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<Ticket>().Property(x => x.FechaCreacion).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<Ticket>().Property(x => x.AnuladoEn).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<Ticket>().Property(x => x.FechaCreacion).HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
        modelBuilder.Entity<Ticket>().Property(x => x.NumeroSecuencial).ValueGeneratedOnAdd();
    }
}
