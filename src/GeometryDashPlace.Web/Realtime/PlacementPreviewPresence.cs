namespace GeometryDashPlace.Web.Realtime;

public sealed class PlacementPreviewPresence(TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(3);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly Dictionary<PreviewKey, DateTimeOffset> _lastSeen = [];

    public void Observe(PlacementPreview preview)
    {
        var key = new PreviewKey(preview.EventId, preview.ActorUserId);
        lock (_gate)
        {
            if (preview.IsActive &&
                (preview.Type is not null ||
                 preview.CursorX is not null && preview.CursorY is not null))
            {
                _lastSeen[key] = _timeProvider.GetUtcNow();
            }
            else
            {
                _lastSeen.Remove(key);
            }
        }
    }

    public IReadOnlyList<PlacementPreview> TakeExpired()
    {
        var cutoff = _timeProvider.GetUtcNow() - Lifetime;
        lock (_gate)
        {
            var expired = _lastSeen
                .Where(pair => pair.Value <= cutoff)
                .Select(pair => pair.Key)
                .ToArray();

            foreach (var key in expired)
            {
                _lastSeen.Remove(key);
            }

            return expired
                .Select(key => new PlacementPreview(
                    key.EventId,
                    key.ActorUserId,
                    IsActive: false))
                .ToArray();
        }
    }

    private readonly record struct PreviewKey(Guid EventId, Guid ActorUserId);
}
