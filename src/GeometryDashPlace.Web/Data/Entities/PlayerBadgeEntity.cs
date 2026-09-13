namespace GeometryDashPlace.Web.Data.Entities;

public sealed class PlayerBadgeEntity
{
    public Guid UserId { get; set; }
    public required string BadgeKey { get; set; }
    public required string ScopeKey { get; set; }
    public Guid? EventId { get; set; }
    public DateTimeOffset UnlockedAt { get; set; }
    public DateTimeOffset? SeenAt { get; set; }
    public UserAccountEntity User { get; set; } = default!;
    public LevelEventEntity? Event { get; set; }
}
