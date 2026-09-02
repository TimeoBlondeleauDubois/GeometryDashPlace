namespace GeometryDashPlace.Web.Components.Editor;

public enum EditorObjectRotationMode
{
    None,
    QuarterTurns,
    Free
}

public sealed record EditorObjectDefinition(
    string Type,
    string Name,
    string ImagePath,
    double YOffset = 0,
    EditorObjectRotationMode RotationMode = EditorObjectRotationMode.Free,
    string? Label = null)
{
    public bool CanRotate => RotationMode is not EditorObjectRotationMode.None;
    public bool CanFreeRotate => RotationMode is EditorObjectRotationMode.Free;
}
