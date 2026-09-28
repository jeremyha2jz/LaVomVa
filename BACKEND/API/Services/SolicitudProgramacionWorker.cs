using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TicketsCombustible.Api.Services;

public sealed class SolicitudProgramacionWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<SolicitudProgramacionWorker> logger) : BackgroundService
{
    private TimeSpan Interval => TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Scheduling:IntervalSeconds", 60), 15, 86_400));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        await ProcessOnceAsync(stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ProcessOnceAsync(stoppingToken);
    }

    private async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var processor = scope.ServiceProvider.GetRequiredService<ISolicitudProgramacionProcessor>();
            var processed = await processor.ProcessDueAsync(cancellationToken);
            if (processed > 0) logger.LogInformation("Scheduler procesó {Count} programaciones vencidas.", processed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            // Leave failed transactions due for the next tick; the processor rolls them back atomically.
            logger.LogError(exception, "No se pudo procesar el lote de solicitudes programadas.");
        }
        try
        {
            var ticketNotifications = scope.ServiceProvider.GetRequiredService<TicketNotificationProcessor>();
            var processed = await ticketNotifications.ProcessDueAsync(cancellationToken);
            if (processed > 0) logger.LogInformation("Evaluó {Count} tickets para notificaciones de vencimiento.", processed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "No se pudieron evaluar las notificaciones de vencimiento de tickets.");
        }
    }
}
