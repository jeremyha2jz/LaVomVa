using System.Collections.Concurrent;
using TicketsCombustible.Api.Services;

namespace TicketsCombustible.Api.Tests;

/// <summary>Installed by ApiTestFactory for every integration test; it has no network capability.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<Exception> failures = new();
    private readonly ConcurrentQueue<TicketEmailMessage> sent = new();
    private Func<CancellationToken, Task>? nextGate;
    public string ProviderName => "FAKE-EMAIL";
    public IReadOnlyList<TicketEmailMessage> Sent => sent.ToArray();
    public void FailNext(string safeMessage, bool outcomeUncertain = false) => failures.Enqueue(new DeliveryProviderException(safeMessage, outcomeUncertain: outcomeUncertain));
    public void GateNext(Func<CancellationToken, Task> gate) => nextGate = gate;
    public void Reset() { while (failures.TryDequeue(out _)) { } while (sent.TryDequeue(out _)) { } nextGate = null; }
    public async Task SendAsync(TicketEmailMessage message, CancellationToken cancellationToken)
    {
        sent.Enqueue(message);
        var gate = Interlocked.Exchange(ref nextGate, null);
        if (gate is not null) await gate(cancellationToken);
        if (failures.TryDequeue(out var error)) throw error;
    }
}

public sealed class FakeSmsSender : ISmsSender
{
    private readonly ConcurrentQueue<Exception> failures = new();
    private readonly ConcurrentQueue<TicketSmsMessage> sent = new();
    public string ProviderName => "FAKE-SMS";
    public IReadOnlyList<TicketSmsMessage> Sent => sent.ToArray();
    public void FailNext(string safeMessage, bool outcomeUncertain = false) => failures.Enqueue(new DeliveryProviderException(safeMessage, outcomeUncertain: outcomeUncertain));
    public void Reset() { while (failures.TryDequeue(out _)) { } while (sent.TryDequeue(out _)) { } }
    public Task SendAsync(TicketSmsMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sent.Enqueue(message);
        if (failures.TryDequeue(out var error)) throw error;
        return Task.CompletedTask;
    }
}
