using GeometryDashPlace.Web.Persistence;

namespace GeometryDashPlace.Web.Realtime;

public sealed record LevelChange(
    Guid EventId,
    Guid ActorUserId,
    string Action,
    long Revision,
    int X,
    int Y,
    int? SourceX,
    int? SourceY,
    DateTimeOffset? NextPlacementAt,
    LevelCell? Cell);
