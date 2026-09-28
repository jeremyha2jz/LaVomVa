using Microsoft.EntityFrameworkCore;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Services;

var connection = Environment.GetEnvironmentVariable("ConnectionStrings__TicketsCombustible")
    ?? throw new InvalidOperationException("E2E test database connection is missing.");
var qrSecret = Environment.GetEnvironmentVariable("Qr__SigningSecret")
    ?? throw new InvalidOperationException("E2E QR signing secret is missing.");
var options = new DbContextOptionsBuilder<TicketsCombustibleDbContext>().UseNpgsql(connection).Options;
await using var db = new TicketsCombustibleDbContext(options);
if (args.Length == 1 && args[0] == "reset")
{
    await db.Database.ExecuteSqlRawAsync("""
        TRUNCATE TABLE auditoria, notificaciones, cierres_diarios, movimientos_inventario,
          ejecuciones_programadas, programaciones_solicitud, detalle_recepciones,
          recepciones_combustible, envios_ticket, despachos, tickets, solicitudes_combustible,
          tanques, estaciones, proveedores, vehiculos, empleados, departamentos, usuarios
          RESTART IDENTITY CASCADE
        """);
    await db.Database.ExecuteSqlRawAsync("UPDATE configuracion_tickets SET secuencia_actual = 0, anio_secuencia = EXTRACT(YEAR FROM CURRENT_DATE)::INTEGER");
    return;
}
if (args.Length != 2 || args[0] != "expire" || !Guid.TryParse(args[1], out var ticketId))
    throw new ArgumentException("Usage: E2eSupport reset | expire <ticket-id>");

var ticket = await db.Tickets.SingleAsync(x => x.Id == ticketId);
ticket.FechaCreacion = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-4), DateTimeKind.Unspecified);
ticket.FechaVencimiento = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-2), DateTimeKind.Unspecified);
ticket.QrHash = TicketQrSignature.Sign(ticket, qrSecret);
await db.SaveChangesAsync();
