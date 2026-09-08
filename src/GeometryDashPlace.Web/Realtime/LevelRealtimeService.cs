using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace GeometryDashPlace.Web.Realtime;

public sealed class LevelRealtimeService(
    IHubContext<LevelHub, ILevelClient> hubContext,
    PlacementPreviewPresence previewPresence,
    ILogger<LevelRealtimeService> logger)
{
    private readonly ConcurrentDictionary<Guid,
        ConcurrentDictionary<Guid, Func<LevelChange, Task>>> _subscribers = [];
    private readonly ConcurrentDictionary<Guid,
        ConcurrentDictionary<Guid, Func<PlacementPreview, Task>>> _previewSubscribers = [];

    public IDisposable Subscribe(Guid eventId, Func<LevelChange, Task> handler)
    {
        var id = Guid.NewGuid();
        _subscribers.GetOrAdd(eventId, _ => [])[id] = handler;
        return new Subscription(() => Unsubscribe(eventId, id));
    }

    public async Task PublishAsync(LevelChange change)
    {
        await PublishToHubAsync(change);
        await PublishToSubscribersAsync(change);
    }

    public IDisposable SubscribeToPreviews(
        Guid eventId,
        Func<PlacementPreview, Task> handler)
    {
        var id = Guid.NewGuid();
        _previewSubscribers.GetOrAdd(eventId, _ => [])[id] = handler;
        return new Subscription(() => UnsubscribePreview(eventId, id));
    }

    public async Task PublishPreviewAsync(PlacementPreview preview)
    {
        previewPresence.Observe(preview);
        await BroadcastPreviewAsync(preview);
    }

    public async Task ExpireInactivePreviewsAsync()
    {
        foreach (var preview in previewPresence.TakeExpired())
        {
            await BroadcastPreviewAsync(preview);
        }
    }

    private async Task BroadcastPreviewAsync(PlacementPreview preview)
    {
        try
        {
            await hubContext.Clients.Group(LevelHub.GroupName(preview.EventId))
                .PlacementPreviewChanged(preview);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "SignalR failed for a placement preview in event {EventId}.",
                preview.EventId);
        }

        if (!_previewSubscribers.TryGetValue(preview.EventId, out var subscribers))
        {
            return;
        }

        foreach (var handler in subscribers.Values)
        {
            try
            {
                await handler(preview);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "A placement preview subscriber failed for event {EventId}.",
                    preview.EventId);
            }
        }
    }

    private async Task PublishToHubAsync(LevelChange change)
    {
        try
        {
            await hubContext.Clients.Group(LevelHub.GroupName(change.EventId))
                .LevelChanged(change);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "SignalR failed for revision {Revision} of event {EventId}.",
                change.Revision, change.EventId);
        }
    }

    private async Task PublishToSubscribersAsync(LevelChange change)
    {
        if (!_subscribers.TryGetValue(change.EventId, out var subscribers))
        {
            return;
        }

        foreach (var handler in subscribers.Values)
        {
            try
            {
                await handler(change);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "A subscriber failed for revision {Revision} of event {EventId}.",
                    change.Revision, change.EventId);
            }
        }
    }

    private void Unsubscribe(Guid eventId, Guid id)
    {
        if (_subscribers.TryGetValue(eventId, out var subscribers))
        {
            subscribers.TryRemove(id, out _);
            if (subscribers.IsEmpty)
            {
                _subscribers.TryRemove(eventId, out _);
            }
        }
    }

    private void UnsubscribePreview(Guid eventId, Guid id)
    {
        if (_previewSubscribers.TryGetValue(eventId, out var subscribers))
        {
            subscribers.TryRemove(id, out _);
            if (subscribers.IsEmpty)
            {
                _previewSubscribers.TryRemove(eventId, out _);
            }
        }
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;
        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
