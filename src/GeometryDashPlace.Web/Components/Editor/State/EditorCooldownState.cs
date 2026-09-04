namespace GeometryDashPlace.Web.Components.Editor.State;

public sealed class EditorCooldownState
{
    private readonly TimeProvider _timeProvider;
    private TimeSpan _serverOffset;

    public EditorCooldownState(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public DateTimeOffset? NextActionAt { get; private set; }
    public TimeSpan Remaining => NextActionAt is { } next
        ? next - (_timeProvider.GetUtcNow() + _serverOffset)
        : TimeSpan.Zero;
    public bool IsReady => Remaining <= TimeSpan.Zero;
    public bool IsUrgent => !IsReady && Remaining <= TimeSpan.FromSeconds(10);
    public string DisplayText
    {
        get
        {
            if (IsReady)
            {
                return "READY";
            }

            var totalSeconds = Math.Max(1, (int)Math.Ceiling(Remaining.TotalSeconds));
            return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
        }
    }

    public void Synchronize(DateTimeOffset serverTime, DateTimeOffset? nextActionAt)
    {
        _serverOffset = serverTime - _timeProvider.GetUtcNow();
        NextActionAt = nextActionAt;
    }

    public void SetNextActionAt(DateTimeOffset? nextActionAt) =>
        NextActionAt = nextActionAt;
}
