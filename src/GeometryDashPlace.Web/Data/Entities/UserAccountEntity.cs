namespace GeometryDashPlace.Web.Data.Entities;

public sealed class UserAccountEntity
{
    public Guid Id { get; set; }
    public required string GoogleSubject { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsEmailVerified { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsBanned { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public ICollection<UserEventStateEntity> EventStates { get; set; } = [];
    public ICollection<LevelCellEntity> Cells { get; set; } = [];
    public ICollection<PlacementHistoryEntity> PlacementHistory { get; set; } = [];
}
