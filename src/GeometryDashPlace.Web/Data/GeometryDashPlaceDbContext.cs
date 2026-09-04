using System.Text.Json;
using GeometryDashPlace.Web.Data.Entities;
using GeometryDashPlace.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GeometryDashPlace.Web.Data;

public sealed class GeometryDashPlaceDbContext(
    DbContextOptions<GeometryDashPlaceDbContext> options) : DbContext(options)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public DbSet<UserAccountEntity> Users => Set<UserAccountEntity>();
    public DbSet<LevelEventEntity> Events => Set<LevelEventEntity>();
    public DbSet<ObjectTypeEntity> ObjectTypes => Set<ObjectTypeEntity>();
    public DbSet<UserEventStateEntity> UserEventStates => Set<UserEventStateEntity>();
    public DbSet<LevelCellEntity> LevelCells => Set<LevelCellEntity>();
    public DbSet<PlacementHistoryEntity> PlacementHistory => Set<PlacementHistoryEntity>();
    public DbSet<LevelSnapshotEntity> LevelSnapshots => Set<LevelSnapshotEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureUser(modelBuilder);
        ConfigureEvent(modelBuilder);
        ConfigureObjectType(modelBuilder);
        ConfigureUserEventState(modelBuilder);
        ConfigureLevelCell(modelBuilder);
        ConfigurePlacementHistory(modelBuilder);
        ConfigureLevelSnapshot(modelBuilder);
    }

    private static void ConfigureUser(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccountEntity>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Id).HasColumnName("id");
            entity.Property(user => user.GoogleSubject).HasColumnName("google_subject").HasMaxLength(255);
            entity.Property(user => user.Email).HasColumnName("email").HasMaxLength(320);
            entity.Property(user => user.DisplayName).HasColumnName("display_name").HasMaxLength(100);
            entity.Property(user => user.AvatarUrl).HasColumnName("avatar_url");
            entity.Property(user => user.IsEmailVerified).HasColumnName("is_email_verified");
            entity.Property(user => user.IsAdmin).HasColumnName("is_admin");
            entity.Property(user => user.IsBanned).HasColumnName("is_banned");
            entity.Property(user => user.CreatedAt).HasColumnName("created_at");
            entity.Property(user => user.LastLoginAt).HasColumnName("last_login_at");
            entity.HasIndex(user => user.GoogleSubject).IsUnique();
        });
    }

    private static void ConfigureEvent(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LevelEventEntity>(entity =>
        {
            entity.ToTable("events");
            entity.HasKey(levelEvent => levelEvent.Id);
            entity.Property(levelEvent => levelEvent.Id).HasColumnName("id");
            entity.Property(levelEvent => levelEvent.Slug).HasColumnName("slug").HasMaxLength(80);
            entity.Property(levelEvent => levelEvent.Name).HasColumnName("name").HasMaxLength(120);
            entity.Property(levelEvent => levelEvent.Description).HasColumnName("description");
            entity.Property(levelEvent => levelEvent.Width).HasColumnName("width");
            entity.Property(levelEvent => levelEvent.Height).HasColumnName("height");
            entity.Property(levelEvent => levelEvent.CooldownSeconds).HasColumnName("cooldown_seconds");
            entity.Property(levelEvent => levelEvent.Status).HasColumnName("status").HasMaxLength(16);
            entity.Property(levelEvent => levelEvent.CurrentRevision)
                .HasColumnName("current_revision")
                .IsConcurrencyToken();
            entity.Property(levelEvent => levelEvent.StartsAt).HasColumnName("starts_at");
            entity.Property(levelEvent => levelEvent.EndsAt).HasColumnName("ends_at");
            entity.Property(levelEvent => levelEvent.CreatedAt).HasColumnName("created_at");
            entity.Property(levelEvent => levelEvent.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(levelEvent => levelEvent.Slug).IsUnique();
        });
    }

    private static void ConfigureObjectType(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ObjectTypeEntity>(entity =>
        {
            entity.ToTable("object_types");
            entity.HasKey(objectType => objectType.Key);
            entity.Property(objectType => objectType.Key).HasColumnName("key").HasMaxLength(64);
            entity.Property(objectType => objectType.DisplayName).HasColumnName("display_name").HasMaxLength(100);
            entity.Property(objectType => objectType.Category).HasColumnName("category").HasMaxLength(32);
            entity.Property(objectType => objectType.GeometryDashObjectId).HasColumnName("geometry_dash_object_id");
            entity.Property(objectType => objectType.YOffset).HasColumnName("y_offset").HasPrecision(8, 3);
            entity.Property(objectType => objectType.RotationMode).HasColumnName("rotation_mode").HasMaxLength(16);
            entity.Property(objectType => objectType.CanScale).HasColumnName("can_scale");
            entity.Property(objectType => objectType.HasColorSettings).HasColumnName("has_color_settings");
            entity.Property(objectType => objectType.HasDurationSetting).HasColumnName("has_duration_setting");
            entity.Property(objectType => objectType.AssetPath).HasColumnName("asset_path");
            entity.Property(objectType => objectType.IsActive).HasColumnName("is_active");
        });
    }

    private static void ConfigureUserEventState(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEventStateEntity>(entity =>
        {
            entity.ToTable("user_event_states");
            entity.HasKey(state => new { state.EventId, state.UserId });
            entity.Property(state => state.EventId).HasColumnName("event_id");
            entity.Property(state => state.UserId).HasColumnName("user_id");
            entity.Property(state => state.PlacementCount).HasColumnName("placement_count");
            entity.Property(state => state.LastPlacementAt).HasColumnName("last_placement_at");
            entity.Property(state => state.NextPlacementAt).HasColumnName("next_placement_at");
            entity.HasOne(state => state.Event).WithMany(levelEvent => levelEvent.UserStates)
                .HasForeignKey(state => state.EventId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(state => state.User).WithMany(user => user.EventStates)
                .HasForeignKey(state => state.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureLevelCell(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LevelCellEntity>(entity =>
        {
            entity.ToTable("level_cells");
            entity.HasKey(cell => new { cell.EventId, cell.X, cell.Y });
            entity.Property(cell => cell.EventId).HasColumnName("event_id");
            entity.Property(cell => cell.X).HasColumnName("x");
            entity.Property(cell => cell.Y).HasColumnName("y");
            entity.Property(cell => cell.ObjectTypeKey).HasColumnName("object_type_key").HasMaxLength(64);
            entity.Property(cell => cell.Rotation).HasColumnName("rotation").HasPrecision(7, 3);
            entity.Property(cell => cell.ScaleX).HasColumnName("scale_x").HasPrecision(6, 3);
            entity.Property(cell => cell.ScaleY).HasColumnName("scale_y").HasPrecision(6, 3);
            entity.Property(cell => cell.ColorRed).HasColumnName("color_red");
            entity.Property(cell => cell.ColorGreen).HasColumnName("color_green");
            entity.Property(cell => cell.ColorBlue).HasColumnName("color_blue");
            entity.Property(cell => cell.DurationSeconds).HasColumnName("duration_seconds").HasPrecision(8, 3);
            entity.Property(cell => cell.AuthorUserId).HasColumnName("author_user_id");
            entity.Property(cell => cell.PlacedAt).HasColumnName("placed_at");
            entity.Property(cell => cell.Revision).HasColumnName("revision");
            entity.HasOne(cell => cell.Event).WithMany(levelEvent => levelEvent.Cells)
                .HasForeignKey(cell => cell.EventId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(cell => cell.ObjectType).WithMany(objectType => objectType.Cells)
                .HasForeignKey(cell => cell.ObjectTypeKey).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(cell => cell.Author).WithMany(user => user.Cells)
                .HasForeignKey(cell => cell.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigurePlacementHistory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlacementHistoryEntity>(entity =>
        {
            entity.ToTable("placement_history");
            entity.HasKey(history => history.Id);
            entity.Property(history => history.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(history => history.EventId).HasColumnName("event_id");
            entity.Property(history => history.Revision).HasColumnName("revision");
            entity.Property(history => history.RequestId).HasColumnName("request_id");
            entity.Property(history => history.UserId).HasColumnName("user_id");
            entity.Property(history => history.X).HasColumnName("x");
            entity.Property(history => history.Y).HasColumnName("y");
            entity.Property(history => history.SourceX).HasColumnName("source_x");
            entity.Property(history => history.SourceY).HasColumnName("source_y");
            entity.Property(history => history.Action).HasColumnName("action").HasMaxLength(16);
            ConfigureCellJson(entity.Property(history => history.PreviousObject).HasColumnName("previous_object"));
            ConfigureCellJson(entity.Property(history => history.NewObject).HasColumnName("new_object"));
            ConfigureCellJson(entity.Property(history => history.ReplacedObject).HasColumnName("replaced_object"));
            entity.Property(history => history.PlacedAt).HasColumnName("placed_at");
            entity.HasIndex(history => history.RequestId).IsUnique();
            entity.HasIndex(history => new { history.EventId, history.Revision }).IsUnique();
            entity.HasOne(history => history.Event).WithMany(levelEvent => levelEvent.PlacementHistory)
                .HasForeignKey(history => history.EventId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(history => history.User).WithMany(user => user.PlacementHistory)
                .HasForeignKey(history => history.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureLevelSnapshot(ModelBuilder modelBuilder)
    {
        var comparer = new ValueComparer<List<LevelCell>>(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            value => value.Aggregate(0, (hash, cell) => HashCode.Combine(hash, cell)),
            value => value.ToList());
        modelBuilder.Entity<LevelSnapshotEntity>(entity =>
        {
            entity.ToTable("level_snapshots");
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(snapshot => snapshot.EventId).HasColumnName("event_id");
            entity.Property(snapshot => snapshot.Revision).HasColumnName("revision");
            entity.Property(snapshot => snapshot.SnapshotType).HasColumnName("snapshot_type").HasMaxLength(16);
            var state = entity.Property(snapshot => snapshot.State)
                .HasColumnName("state")
                .HasColumnType("jsonb")
                .HasConversion(
                    value => SerializeCells(value),
                    value => DeserializeCells(value));
            state.Metadata.SetValueComparer(comparer);
            entity.Property(snapshot => snapshot.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(snapshot => new { snapshot.EventId, snapshot.Revision }).IsUnique();
            entity.HasOne(snapshot => snapshot.Event).WithMany(levelEvent => levelEvent.Snapshots)
                .HasForeignKey(snapshot => snapshot.EventId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureCellJson(
        Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<LevelCell?> property) =>
        property.HasColumnType("jsonb").HasConversion(
            value => SerializeCell(value),
            value => DeserializeCell(value));

    private static string? SerializeCell(LevelCell? cell) =>
        cell is null ? null : JsonSerializer.Serialize(cell, JsonOptions);

    private static LevelCell? DeserializeCell(string? json) =>
        json is null ? null : JsonSerializer.Deserialize<LevelCell>(json, JsonOptions);

    private static string SerializeCells(List<LevelCell> cells) =>
        JsonSerializer.Serialize(cells, JsonOptions);

    private static List<LevelCell> DeserializeCells(string json) =>
        JsonSerializer.Deserialize<List<LevelCell>>(json, JsonOptions) ?? [];
}
