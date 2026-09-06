namespace GeometryDashPlace.Web.Realtime;

public sealed record PlacementPreview(
    Guid EventId,
    Guid ActorUserId,
    bool IsActive,
    string? Type = null,
    int X = 0,
    int Y = 0,
    double Rotation = 0,
    double ScaleX = 1,
    double ScaleY = 1,
    int Red = 255,
    int Green = 255,
    int Blue = 255,
    double Duration = 0.2);
