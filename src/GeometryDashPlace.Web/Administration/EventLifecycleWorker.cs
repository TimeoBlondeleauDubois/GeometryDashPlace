using GeometryDashPlace.Web.Events;
using GeometryDashPlace.Web.Profiles;

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
            var badges = scope.ServiceProvider.GetRequiredService<IPlayerBadgeService>();
            var awardedBadges = await badges.AwardCompletedEventBadgesAsync(cancellationToken);
            if (closed > 0)
            {
                logger.LogInformation("Automatically closed {EventCount} expired events.", closed);
            }
            if (awardedBadges > 0)
            {
                logger.LogInformation(
                    "Awarded {BadgeCount} event ranking badges.", awardedBadges);
            }
            await lifecycleNotifier.PublishAsync();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Unable to process expired events.");
        }
    }
}
