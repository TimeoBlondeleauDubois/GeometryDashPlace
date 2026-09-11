namespace GeometryDashPlace.Web.Components.Editor.State;

public static class EditorSceneColorCalculator
{
    public const double SlowSpeedCellsPerSecond = 8.371978759765625;
    public const double NormalSpeedCellsPerSecond = 10.385991096496582;
    public const double FastSpeedCellsPerSecond = 12.914042472839355;
    public const double VeryFastSpeedCellsPerSecond = 15.600034713745117;
    public const double ExtremelyFastSpeedCellsPerSecond = 19.199991226196289;

    private static readonly SceneRgb DefaultBackground = new(25, 117, 220);
    private static readonly SceneRgb DefaultGround = new(5, 126, 255);

    public static EditorSceneColors Calculate(
        IEnumerable<EditorObjectInstance> objects,
        double viewportCenterX)
    {
        var background = new ColorTrack(DefaultBackground);
        var ground = new ColorTrack(DefaultGround);
        var speed = NormalSpeedCellsPerSecond;
        var elapsedSeconds = 0d;
        var previousX = 0d;

        var events = objects
            .Select(instance => new SceneEvent(instance, instance.X + 0.5))
            .Where(sceneEvent => sceneEvent.X <= viewportCenterX &&
                (TargetFor(sceneEvent.Instance) is not null || SpeedFor(sceneEvent.Instance.Type) is not null))
            .OrderBy(sceneEvent => sceneEvent.X)
            .ThenBy(sceneEvent => sceneEvent.Instance.Y)
            .ToArray();

        foreach (var positionGroup in events.GroupBy(sceneEvent => sceneEvent.X))
        {
            var position = positionGroup.Key;
            elapsedSeconds += Math.Max(0, position - previousX) / speed;

            foreach (var sceneEvent in positionGroup)
            {
                var instance = sceneEvent.Instance;
                switch (TargetFor(instance))
                {
                    case "background":
                        background.StartTransition(instance, elapsedSeconds);
                        break;
                    case "ground":
                        ground.StartTransition(instance, elapsedSeconds);
                        break;
                }
            }

            foreach (var speedPortal in positionGroup
                .Select(sceneEvent => SpeedFor(sceneEvent.Instance.Type))
                .OfType<double>())
            {
                speed = speedPortal;
            }

            previousX = position;
        }

        elapsedSeconds += Math.Max(0, viewportCenterX - previousX) / speed;
        return new EditorSceneColors(
            background.GetPreview(elapsedSeconds),
            ground.GetPreview(elapsedSeconds));
    }

    private static string? TargetFor(EditorObjectInstance instance) => instance.Type switch
    {
        "bg_color_trigger" => "background",
        "g1_color_trigger" => "ground",
        "color_trigger" => instance.ColorTarget,
        _ => null
    };

    private static double? SpeedFor(string type) => type switch
    {
        "slow_speed" => SlowSpeedCellsPerSecond,
        "normal_speed" => NormalSpeedCellsPerSecond,
        "fast_speed" => FastSpeedCellsPerSecond,
        "very_fast_speed" => VeryFastSpeedCellsPerSecond,
        "extremely_fast_speed" => ExtremelyFastSpeedCellsPerSecond,
        _ => null
    };

    private sealed class ColorTrack(SceneRgb initialColor)
    {
        private readonly List<ScheduledTransition> _transitions = [];
        private SceneRgb _visibleColor = initialColor;
        private bool _hasTrigger;
        private int _lastTriggerX;
        private int _lastTriggerY;

        public void StartTransition(EditorObjectInstance trigger, double elapsedSeconds)
        {
            AdvanceTo(elapsedSeconds);

            var target = new SceneRgb(
                Math.Clamp(trigger.Red, 0, 255),
                Math.Clamp(trigger.Green, 0, 255),
                Math.Clamp(trigger.Blue, 0, 255));
            var duration = Math.Max(0, trigger.Duration);

            _transitions.Add(new ScheduledTransition(
                _visibleColor,
                target,
                elapsedSeconds,
                elapsedSeconds + duration,
                trigger.X,
                trigger.Y));
            _hasTrigger = true;
            _lastTriggerX = trigger.X;
            _lastTriggerY = trigger.Y;

            // An instantaneous trigger must complete before another trigger at
            // the same position is evaluated.
            AdvanceTo(elapsedSeconds);
        }

        public EditorSceneColor? GetPreview(double elapsedSeconds)
        {
            if (!_hasTrigger)
            {
                return null;
            }

            AdvanceTo(elapsedSeconds);
            var source = _transitions.Count > 0 ? _transitions[^1] : null;
            return new EditorSceneColor(
                _visibleColor.Red,
                _visibleColor.Green,
                _visibleColor.Blue,
                source?.TriggerX ?? _lastTriggerX,
                source?.TriggerY ?? _lastTriggerY);
        }

        private void AdvanceTo(double elapsedSeconds)
        {
            while (_transitions.Count > 0)
            {
                var active = _transitions[^1];
                if (active.EndsAt > elapsedSeconds)
                {
                    _visibleColor = active.ColorAt(elapsedSeconds);
                    return;
                }

                var completedAt = active.EndsAt;
                _visibleColor = active.Target;
                _lastTriggerX = active.TriggerX;
                _lastTriggerY = active.TriggerY;
                _transitions.RemoveAt(_transitions.Count - 1);

                // Covered transitions continue consuming their original time.
                // If one is still running when the covering trigger finishes,
                // it resumes from the newly visible color and keeps its original
                // end time. Already expired covered transitions are discarded.
                while (_transitions.Count > 0 && _transitions[^1].EndsAt <= completedAt)
                {
                    _transitions.RemoveAt(_transitions.Count - 1);
                }

                if (_transitions.Count > 0)
                {
                    _transitions[^1].ResumeFrom(_visibleColor, completedAt);
                }
            }
        }

        private static double Interpolate(double from, double to, double progress) =>
            from + (to - from) * progress;

        private sealed class ScheduledTransition(
            SceneRgb from,
            SceneRgb target,
            double startsAt,
            double endsAt,
            int triggerX,
            int triggerY)
        {
            public SceneRgb From { get; private set; } = from;
            public SceneRgb Target { get; } = target;
            public double StartsAt { get; private set; } = startsAt;
            public double EndsAt { get; } = endsAt;
            public int TriggerX { get; } = triggerX;
            public int TriggerY { get; } = triggerY;

            public SceneRgb ColorAt(double elapsedSeconds)
            {
                var duration = EndsAt - StartsAt;
                var progress = duration <= 0
                    ? 1
                    : Math.Clamp((elapsedSeconds - StartsAt) / duration, 0, 1);
                return new SceneRgb(
                    Interpolate(From.Red, Target.Red, progress),
                    Interpolate(From.Green, Target.Green, progress),
                    Interpolate(From.Blue, Target.Blue, progress));
            }

            public void ResumeFrom(SceneRgb color, double elapsedSeconds)
            {
                From = color;
                StartsAt = elapsedSeconds;
            }
        }
    }

    private sealed record SceneEvent(EditorObjectInstance Instance, double X);
    private sealed record SceneRgb(double Red, double Green, double Blue);
}

public sealed record EditorSceneColors(
    EditorSceneColor? Background,
    EditorSceneColor? Ground);
