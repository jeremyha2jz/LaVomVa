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
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Despacho> Despachos => Set<Despacho>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Rol> Roles => Set<Rol>();
    public DbSet<UsuarioRol> UsuarioRoles => Set<UsuarioRol>();
    public DbSet<MovimientoInventario> MovimientosInventario => Set<MovimientoInventario>();
    public DbSet<Proveedor> Proveedores => Set<Proveedor>();
    public DbSet<RecepcionCombustible> Recepciones => Set<RecepcionCombustible>();
    public DbSet<DetalleRecepcion> DetallesRecepcion => Set<DetalleRecepcion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Departamento>().ToTable("departamentos").HasKey(x => x.Id);
        modelBuilder.Entity<Empleado>().ToTable("empleados").HasKey(x => x.Id);
        modelBuilder.Entity<Vehiculo>().ToTable("vehiculos").HasKey(x => x.Id);
        modelBuilder.Entity<TipoCombustible>().ToTable("tipos_combustible").HasKey(x => x.Id);
        modelBuilder.Entity<Tanque>().ToTable("tanques").HasKey(x => x.Id);
        modelBuilder.Entity<Estacion>().ToTable("estaciones").HasKey(x => x.Id);
        modelBuilder.Entity<SolicitudCombustible>().ToTable("solicitudes_combustible").HasKey(x => x.Id);
        modelBuilder.Entity<Ticket>().ToTable("tickets").HasKey(x => x.Id);
        modelBuilder.Entity<Despacho>().ToTable("despachos").HasKey(x => x.Id);
        modelBuilder.Entity<Usuario>().ToTable("usuarios").HasKey(x => x.Id);
        modelBuilder.Entity<Rol>().ToTable("roles").HasKey(x => x.Id);
        modelBuilder.Entity<UsuarioRol>().ToTable("usuario_roles").HasKey(x => new { x.UsuarioId, x.RolId });
        modelBuilder.Entity<MovimientoInventario>().ToTable("movimientos_inventario").HasKey(x => x.Id);
        modelBuilder.Entity<MovimientoInventario>().Property(x => x.FechaHora).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<Proveedor>().ToTable("proveedores").HasKey(x => x.Id);
        modelBuilder.Entity<RecepcionCombustible>().ToTable("recepciones_combustible").HasKey(x => x.Id);
        modelBuilder.Entity<RecepcionCombustible>().Property(x => x.FechaRecepcion).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<DetalleRecepcion>().ToTable("detalle_recepciones").HasKey(x => x.Id);
        modelBuilder.Entity<SolicitudCombustible>().Property(x => x.Estado).HasConversion<string>();
        modelBuilder.Entity<SolicitudCombustible>().Property(x => x.FechaSolicitud).HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
        modelBuilder.Entity<SolicitudCombustible>().Property(x => x.FechaVencimiento).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<SolicitudCombustible>().Property(x => x.FechaAprobacion).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<Ticket>().Property(x => x.Estado).HasConversion<string>();
        modelBuilder.Entity<Ticket>().Property(x => x.FechaVencimiento).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<Ticket>().Property(x => x.FechaCreacion).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<Ticket>().Property(x => x.FechaCreacion).HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
        modelBuilder.Entity<Ticket>().Property(x => x.NumeroSecuencial).ValueGeneratedOnAdd();
    }
}
