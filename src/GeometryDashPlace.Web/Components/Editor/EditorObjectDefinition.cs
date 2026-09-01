namespace GeometryDashPlace.Web.Components.Editor;

public sealed record EditorObjectDefinition(
    string Type,
    string Name,
    string ImagePath,
    double YOffset = 0,
    bool CanRotate = true,
    string? Label = null);
