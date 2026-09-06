using GeometryDashPlace.Web.Events;

namespace GeometryDashPlace.Web.Administration;

public sealed class EventLifecycleWorker(
    IServiceScopeFactory scopeFactory,
    EventLifecycleNotifier lifecycleNotifier,
    ILogger<EventLifecycleWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CloseExpiredEventsSafelyAsync(stoppingToken);
        using var timer = new PeriodicTimer(CheckInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CloseExpiredEventsSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task CloseExpiredEventsSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var lifecycle = scope.ServiceProvider.GetRequiredService<IEventLifecycleService>();
            var closed = await lifecycle.CloseExpiredEventsAsync(cancellationToken);
            if (closed > 0)
            {
                logger.LogInformation("Automatically closed {EventCount} expired events.", closed);
            }
            await lifecycleNotifier.PublishAsync();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Unable to process expired events.");
        }
    }
}
