using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TicketsCombustible.Api.Services;
using Xunit;

namespace TicketsCombustible.Api.Tests;

public sealed class SolicitudProgramacionWorkerTests
{
    [Fact]
    public async Task Worker_procesa_al_arrancar_y_se_cancela_sin_esperar_el_intervalo()
    {
        var processor = new RecordingProcessor();
        var services = new ServiceCollection();
        services.AddScoped<ISolicitudProgramacionProcessor>(_ => processor);
        await using var provider = services.BuildServiceProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Scheduling:IntervalSeconds"] = "60"
        }).Build();
        var worker = new SolicitudProgramacionWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            configuration, TimeProvider.System, NullLogger<SolicitudProgramacionWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await processor.FirstCall.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, processor.Calls);
        Assert.False(processor.FirstToken.IsCancellationRequested);
        await worker.StopAsync(CancellationToken.None);
        worker.Dispose();
    }

    private sealed class RecordingProcessor : ISolicitudProgramacionProcessor
    {
        public int Calls { get; private set; }
        public CancellationToken FirstToken { get; private set; }
        public TaskCompletionSource FirstCall { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<int> ProcessDueAsync(CancellationToken cancellationToken)
        {
            Calls++;
            FirstToken = cancellationToken;
            FirstCall.TrySetResult();
            return Task.FromResult(0);
        }
    }
}
