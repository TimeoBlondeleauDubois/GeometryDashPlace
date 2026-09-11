namespace GeometryDashPlace.Web.Persistence;

public sealed record LevelCell(
    int X,
    int Y,
    string Type,
    double Rotation,
    double ScaleX,
    double ScaleY,
    int? Red,
    int? Green,
    int? Blue,
    double? Duration,
    Guid AuthorUserId,
    string Author,
    long Revision,
    DateTimeOffset PlacedAt);

public sealed record LevelState(
    Guid EventId,
    long Revision,
    IReadOnlyList<LevelCell> Cells);

public sealed record LevelRevisionDetails(
    long Revision,
    string Action,
    int X,
    int Y,
    int? SourceX,
    int? SourceY,
    string? ObjectType,
    LevelCell? Cell,
    Guid AuthorUserId,
    string Author,
    string? AuthorAvatarUrl,
    DateTimeOffset PlacedAt);

public sealed record LevelCooldownState(
    DateTimeOffset ServerTime,
    DateTimeOffset? NextPlacementAt);

public sealed record PlaceLevelCellRequest(
    Guid RequestId,
    string Type,
    double Rotation = 0,
    double ScaleX = 1,
    double ScaleY = 1,
    int? Red = null,
    int? Green = null,
    int? Blue = null,
    double? Duration = null,
    bool ConfirmRecentOverwrite = false);

public sealed record DeleteLevelCellRequest(Guid RequestId);

public sealed record MoveLevelCellRequest(
    Guid RequestId,
    int TargetX,
    int TargetY,
    string Type,
    double Rotation = 0,
    double ScaleX = 1,
    double ScaleY = 1,
    int? Red = null,
    int? Green = null,
    int? Blue = null,
    double? Duration = null,
    bool ConfirmRecentOverwrite = false)
{
    public PlaceLevelCellRequest ToPlacement() => new(
        RequestId, Type, Rotation, ScaleX, ScaleY, Red, Green, Blue, Duration,
        ConfirmRecentOverwrite);
}

public sealed record LevelMutation(
    string Action,
    long Revision,
    DateTimeOffset? NextPlacementAt,
    LevelCell? Cell,
    bool IsReplay = false);
