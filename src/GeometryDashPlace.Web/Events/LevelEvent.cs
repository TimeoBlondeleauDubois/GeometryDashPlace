namespace GeometryDashPlace.Web.Events;

public sealed record LevelEvent(
    Guid Id,
    string Slug,
    string Name,
    string? Description,
    int Width,
    int Height,
    int CooldownSeconds,
    long Revision);
