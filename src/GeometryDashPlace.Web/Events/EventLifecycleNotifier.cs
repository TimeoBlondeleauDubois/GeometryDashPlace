using System.Collections.Concurrent;

namespace GeometryDashPlace.Web.Events;

public sealed class EventLifecycleNotifier(ILogger<EventLifecycleNotifier> logger)
{
    private readonly ConcurrentDictionary<Guid, Func<Task>> _subscribers = [];

    public IDisposable Subscribe(Func<Task> handler)
    {
        var subscriptionId = Guid.NewGuid();
        _subscribers[subscriptionId] = handler;
        return new Subscription(() => _subscribers.TryRemove(subscriptionId, out _));
    }

    public async Task PublishAsync()
    {
        foreach (var subscriber in _subscribers.Values)
        {
            try
            {
                await subscriber();
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "An event lifecycle subscriber failed.");
            }
        }
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;

        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
