namespace TicketsCombustible.Api.Contracts;

public sealed record InventoryUpdatedEvent(
    long TankId,
    long StationId,
    decimal PreviousQuantity,
    decimal CurrentQuantity,
    decimal Capacity,
    decimal Percentage,
    bool Critical,
    string MovementType,
    decimal Quantity,
    long MovementId,
    DateTimeOffset OccurredAt);

public sealed record InventoryMovementCreatedEvent(
    long MovementId,
    long TankId,
    string TankCode,
    long StationId,
    string MovementType,
    decimal Quantity,
    decimal PreviousQuantity,
    decimal CurrentQuantity,
    string? ReferenceType,
    string? ReferenceId,
    DateTimeOffset OccurredAt);

public sealed record CriticalInventoryChangedEvent(
    long TankId,
    long StationId,
    bool Critical,
    decimal CurrentQuantity,
    decimal CriticalLevel,
    long MovementId,
    DateTimeOffset OccurredAt);

public sealed record InventoryRealtimeEvent(
    InventoryUpdatedEvent Updated,
    InventoryMovementCreatedEvent Movement,
    CriticalInventoryChangedEvent? CriticalChange);
