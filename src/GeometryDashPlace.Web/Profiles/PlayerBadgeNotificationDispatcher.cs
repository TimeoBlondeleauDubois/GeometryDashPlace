using System.Collections.Concurrent;

namespace GeometryDashPlace.Web.Profiles;

public sealed class PlayerBadgeNotificationDispatcher(
    ILogger<PlayerBadgeNotificationDispatcher> logger)
{
    private readonly ConcurrentDictionary<Guid, Func<IReadOnlyList<PlayerBadgeNotification>, Task>>
        _subscribers = [];

    public IDisposable Subscribe(Func<IReadOnlyList<PlayerBadgeNotification>, Task> handler)
    {
        var subscriptionId = Guid.NewGuid();
        _subscribers[subscriptionId] = handler;
        return new Subscription(() => _subscribers.TryRemove(subscriptionId, out _));
    }

    public async Task PublishAsync(IReadOnlyList<PlayerBadgeNotification> badges)
    {
        if (badges.Count == 0)
        {
            return;
        }

        foreach (var subscriber in _subscribers.Values)
        {
            try
            {
                await subscriber(badges);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "A badge notification subscriber failed.");
            }
        }
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;

        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
