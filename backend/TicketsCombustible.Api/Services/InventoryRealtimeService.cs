using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TicketsCombustible.Api.Contracts;
using TicketsCombustible.Api.Data;
using TicketsCombustible.Api.Hubs;

namespace TicketsCombustible.Api.Services;

public interface IInventoryEventSink
{
    Task PublishAsync(InventoryRealtimeEvent inventoryEvent, CancellationToken cancellationToken = default);
}

public interface IInventoryRealtimePublisher
{
    Task PublishMovementAsync(long movementId, CancellationToken cancellationToken = default);
}

public sealed class SignalRInventoryEventSink(IHubContext<InventoryHub> hub, ILogger<SignalRInventoryEventSink> logger) : IInventoryEventSink
{
    public async Task PublishAsync(InventoryRealtimeEvent inventoryEvent, CancellationToken cancellationToken = default)
    {
        await SendAsync("InventoryUpdated", inventoryEvent.Updated, cancellationToken);
        await SendAsync("InventoryMovementCreated", inventoryEvent.Movement, cancellationToken);
        if (inventoryEvent.CriticalChange is not null)
            await SendAsync("CriticalInventoryChanged", inventoryEvent.CriticalChange, cancellationToken);
    }

    private async Task SendAsync(string eventName, object payload, CancellationToken cancellationToken)
    {
        try { await hub.Clients.All.SendAsync(eventName, payload, cancellationToken); }
        catch (Exception exception) { logger.LogWarning(exception, "SignalR no pudo emitir el evento {EventName}.", eventName); }
    }
}

public sealed class InventoryRealtimePublisher(
    TicketsCombustibleDbContext db,
    IInventoryEventSink sink,
    ILogger<InventoryRealtimePublisher> logger) : IInventoryRealtimePublisher
{
    public async Task PublishMovementAsync(long movementId, CancellationToken cancellationToken = default)
    {
        try
        {
            var persisted = await (from row in db.MovimientosInventario.AsNoTracking()
                join tank in db.Tanques.AsNoTracking() on row.TanqueId equals tank.Id
                where row.Id == movementId
                select new
                {
                    Movement = row,
                    tank.Codigo,
                    tank.EstacionId,
                    tank.CapacidadGalones,
                    tank.NivelCriticoGalones
                }).SingleOrDefaultAsync(cancellationToken);

            if (persisted is null || persisted.Movement.ExistenciaAnterior is null || persisted.Movement.ExistenciaNueva is null)
            {
                logger.LogWarning("No se pudo emitir el movimiento {MovementId}: no se encontró su snapshot persistido.", movementId);
                return;
            }

            var movement = persisted.Movement;
            var previous = movement.ExistenciaAnterior.Value;
            var current = movement.ExistenciaNueva.Value;
            var critical = current <= persisted.NivelCriticoGalones;
            var occurredAt = new DateTimeOffset(DateTime.SpecifyKind(movement.FechaHora, DateTimeKind.Utc));
            var updated = new InventoryUpdatedEvent(
                movement.TanqueId,
                persisted.EstacionId,
                previous,
                current,
                persisted.CapacidadGalones,
                persisted.CapacidadGalones == 0 ? 0 : decimal.Round(current / persisted.CapacidadGalones * 100, 2),
                critical,
                movement.TipoMovimiento,
                movement.CantidadGalones,
                movement.Id,
                occurredAt);
            var created = new InventoryMovementCreatedEvent(
                movement.Id,
                movement.TanqueId,
                persisted.Codigo,
                persisted.EstacionId,
                movement.TipoMovimiento,
                movement.CantidadGalones,
                previous,
                current,
                movement.ReferenciaTipo,
                movement.ReferenciaId,
                occurredAt);
            var previousCritical = previous <= persisted.NivelCriticoGalones;
            var criticalChange = previousCritical == critical ? null : new CriticalInventoryChangedEvent(
                movement.TanqueId,
                persisted.EstacionId,
                critical,
                current,
                persisted.NivelCriticoGalones,
                movement.Id,
                occurredAt);

            try
            {
                await sink.PublishAsync(new InventoryRealtimeEvent(updated, created, criticalChange), cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "SignalR no pudo publicar el movimiento {MovementId}; la operación ya estaba confirmada.", movementId);
            }
        }
        catch (Exception exception)
        {
            // This runs after the business transaction commits. Realtime delivery must never change its result.
            logger.LogWarning(exception, "No se pudo leer/publicar el snapshot del movimiento {MovementId} después del commit.", movementId);
        }
    }
}
