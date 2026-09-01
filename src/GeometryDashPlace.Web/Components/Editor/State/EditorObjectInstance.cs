namespace GeometryDashPlace.Web.Components.Editor.State;

public sealed class EditorObjectInstance
{
    public required string Type { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Rotation { get; set; }
    public string? ColorTarget { get; set; }
    public int Red { get; set; } = 255;
    public int Green { get; set; } = 255;
    public int Blue { get; set; } = 255;
    public double Duration { get; set; } = 0.2;

    public EditorObjectInstance Clone() => new()
    {
        Type = Type,
        X = X,
        Y = Y,
        Rotation = Rotation,
        ColorTarget = ColorTarget,
        Red = Red,
        Green = Green,
        Blue = Blue,
        Duration = Duration
    };
}
