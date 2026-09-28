using System.Collections.Concurrent;
using TicketsCombustible.Api.Contracts;
using TicketsCombustible.Api.Services;

namespace TicketsCombustible.Api.Tests;

public sealed class CapturingInventoryEventSink : IInventoryEventSink
{
    private readonly ConcurrentQueue<InventoryRealtimeEvent> events = new();
    public bool FailPublishes { get; set; }
    public IReadOnlyList<InventoryRealtimeEvent> Events => events.ToArray();

    public Task PublishAsync(InventoryRealtimeEvent inventoryEvent, CancellationToken cancellationToken = default)
    {
        if (FailPublishes) throw new InvalidOperationException("Test sink unavailable.");
        events.Enqueue(inventoryEvent);
        return Task.CompletedTask;
    }

    public void Reset()
    {
        while (events.TryDequeue(out _)) { }
        FailPublishes = false;
    }
}

public sealed class TestingInventoryEventSink(
    SignalRInventoryEventSink actual,
    CapturingInventoryEventSink capture) : IInventoryEventSink
{
    public async Task PublishAsync(InventoryRealtimeEvent inventoryEvent, CancellationToken cancellationToken = default)
    {
        await capture.PublishAsync(inventoryEvent, cancellationToken);
        await actual.PublishAsync(inventoryEvent, cancellationToken);
    }
}
