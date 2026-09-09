using Microsoft.AspNetCore.Components.Server.Circuits;

namespace GeometryDashPlace.Web.Realtime;

public sealed class EditorCircuitPresence(
    LevelRealtimeService realtime) : CircuitHandler
{
    private readonly object _gate = new();
    private PreviewOwner? _previewOwner;
    private int _isConnected = 1;

    public bool IsConnected => Volatile.Read(ref _isConnected) == 1;

    public void Track(Guid eventId, Guid userId)
    {
        lock (_gate)
        {
            _previewOwner = new PreviewOwner(eventId, userId);
        }
    }

    public void StopTracking()
    {
        lock (_gate)
        {
            _previewOwner = null;
        }
    }

    public override Task OnConnectionUpAsync(
        Circuit circuit,
        CancellationToken cancellationToken)
    {
        Volatile.Write(ref _isConnected, 1);
        return Task.CompletedTask;
    }

    public override async Task OnConnectionDownAsync(
        Circuit circuit,
        CancellationToken cancellationToken)
    {
        Volatile.Write(ref _isConnected, 0);
        PreviewOwner? owner;
        lock (_gate)
        {
            owner = _previewOwner;
        }

        if (owner is not null)
        {
            await realtime.PublishPreviewAsync(new PlacementPreview(
                owner.EventId,
                owner.UserId,
                IsActive: false));
        }
    }

    private sealed record PreviewOwner(Guid EventId, Guid UserId);
}
