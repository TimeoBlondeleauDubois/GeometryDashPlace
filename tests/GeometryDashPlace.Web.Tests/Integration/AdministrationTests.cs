using GeometryDashPlace.Web.Administration;
using GeometryDashPlace.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GeometryDashPlace.Web.Tests.Integration;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class AdministrationTests(PostgreSqlIntegrationFixture database)
{
    [PostgreSqlFact]
    public async Task NonAdmin_CannotUseAdministrationService()
    {
        var scenario = await database.CreateScenarioAsync(eventStatus: "closed");
        using var scope = database.Application.Services.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<IAdministrationService>();

        var exception = await Assert.ThrowsAsync<AdministrationException>(
            () => administration.GetEventsAsync(scenario.UserId));

        Assert.Equal("admin_required", exception.Code);
        Assert.Equal(403, exception.StatusCode);
    }

    [PostgreSqlFact]
    public async Task Admin_CanManageUsersAndEventLifecycle()
    {
        var scenario = await database.CreateScenarioAsync(
            userCount: 2,
            eventStatus: "closed",
            isAdmin: true);
        using var scope = database.Application.Services.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<IAdministrationService>();
        var suffix = Guid.NewGuid().ToString("N");
        var startsAt = DateTimeOffset.UtcNow.AddHours(2);
        var endsAt = startsAt.AddHours(3);
        var input = new AdminEventInput(
            $"admin-{suffix}",
            "Administration test event",
            "Created through the secured administration service.",
            90,
            startsAt,
            endsAt,
            "background-02",
            "ground-02",
            Width: 192,
            Height: 24);

        await administration.SetAdminAsync(
            scenario.UserId,
            scenario.UserIds[1],
            isAdmin: true);
        var created = await administration.CreateEventAsync(scenario.UserId, input);

        Assert.Equal("open", created.Status);
        Assert.Equal(192, created.Width);
        Assert.Equal(24, created.Height);
        Assert.Equal("background-02", created.BackgroundKey);
        Assert.Equal("ground-02", created.GroundKey);
        await using (var context = database.CreateDbContext())
        {
            Assert.True(await context.Users
                .Where(user => user.Id == scenario.UserIds[1])
                .Select(user => user.IsAdmin)
                .SingleAsync());
        }

        var overlap = await Assert.ThrowsAsync<AdministrationException>(() =>
            administration.CreateEventAsync(
                scenario.UserId,
                input with
                {
                    Slug = $"overlap-{suffix}",
                    Name = "Overlapping event"
                }));
        Assert.Equal("event_schedule_overlap", overlap.Code);

        var dimensionsLocked = await Assert.ThrowsAsync<AdministrationException>(() =>
            administration.UpdateEventAsync(
                scenario.UserId,
                created.Id,
                input with { Width = 193 }));
        Assert.Equal("event_dimensions_locked", dimensionsLocked.Code);

        var completedInput = input with
        {
            StartsAt = DateTimeOffset.UtcNow.AddHours(-2),
            EndsAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        var closed = await administration.UpdateEventAsync(
            scenario.UserId,
            created.Id,
            completedInput);
        Assert.Equal("closed", closed.Status);

        await using (var context = database.CreateDbContext())
        {
            var snapshot = await context.LevelSnapshots
                .SingleAsync(candidate => candidate.EventId == created.Id);
            Assert.Equal("final", snapshot.SnapshotType);
            Assert.Equal(0, snapshot.Revision);
            Assert.Empty(snapshot.State);
        }

        var renamed = await administration.UpdateCompletedEventDetailsAsync(
            scenario.UserId,
            created.Id,
            new AdminEventDetailsInput(
                input.Slug,
                "Renamed completed event",
                "Updated after completion."));
        Assert.Equal("Renamed completed event", renamed.Name);

        var locked = await Assert.ThrowsAsync<AdministrationException>(() =>
            administration.UpdateEventAsync(
                scenario.UserId,
                created.Id,
                completedInput with { CooldownSeconds = 91 }));
        Assert.Equal("completed_event_locked", locked.Code);

        var selfDemotion = await Assert.ThrowsAsync<AdministrationException>(() =>
            administration.SetAdminAsync(
                scenario.UserId,
                scenario.UserId,
                isAdmin: false));
        Assert.Equal("self_demotion", selfDemotion.Code);
    }

    [PostgreSqlFact]
    public async Task ExpiredOpenEvent_IsClosedWithFinalSnapshot()
    {
        var scenario = await database.CreateScenarioAsync(eventStatus: "open");
        await using (var context = database.CreateDbContext())
        {
            var levelEvent = await context.Events.SingleAsync(
                candidate => candidate.Id == scenario.EventId);
            levelEvent.StartsAt = DateTimeOffset.UtcNow.AddHours(-2);
            levelEvent.EndsAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await context.SaveChangesAsync();
        }

        using var scope = database.Application.Services.CreateScope();
        var lifecycle = scope.ServiceProvider.GetRequiredService<IEventLifecycleService>();
        await lifecycle.CloseExpiredEventsAsync();

        await using var verification = database.CreateDbContext();
        var closedEvent = await verification.Events.AsNoTracking().SingleAsync(
            candidate => candidate.Id == scenario.EventId);
        var snapshot = await verification.LevelSnapshots.AsNoTracking().SingleAsync(
            candidate => candidate.EventId == scenario.EventId);
        Assert.Equal("closed", closedEvent.Status);
        Assert.Equal("final", snapshot.SnapshotType);
        Assert.Equal(closedEvent.CurrentRevision, snapshot.Revision);
    }

    [PostgreSqlFact]
    public async Task StartedEvent_StartDateCannotBeChanged()
    {
        var scenario = await database.CreateScenarioAsync(isAdmin: true);
        await using var context = database.CreateDbContext();
        var levelEvent = await context.Events.AsNoTracking().SingleAsync(
            candidate => candidate.Id == scenario.EventId);

        using var scope = database.Application.Services.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<IAdministrationService>();
        var input = new AdminEventInput(
            levelEvent.Slug,
            levelEvent.Name,
            levelEvent.Description,
            levelEvent.CooldownSeconds,
            levelEvent.StartsAt?.AddMinutes(-1),
            levelEvent.EndsAt);

        var exception = await Assert.ThrowsAsync<AdministrationException>(() =>
            administration.UpdateEventAsync(scenario.UserId, scenario.EventId, input));

        Assert.Equal("event_start_locked", exception.Code);
    }

    [PostgreSqlFact]
    public async Task Admin_CanBanUserAndExistingSessionCannotPlace()
    {
        var scenario = await database.CreateScenarioAsync(
            userCount: 2,
            isAdmin: true);
        using var scope = database.Application.Services.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<IAdministrationService>();
        var levels = scope.ServiceProvider.GetRequiredService<ILevelRepository>();

        await administration.SetBannedAsync(
            scenario.UserIds[0],
            scenario.UserIds[1],
            isBanned: true);

        var exception = await Assert.ThrowsAsync<LevelPersistenceException>(() =>
            levels.PlaceAsync(
                scenario.EventId,
                scenario.UserIds[1],
                1,
                1,
                new PlaceLevelCellRequest(Guid.NewGuid(), "block")));
        Assert.Equal("user_banned", exception.Code);

        await using var context = database.CreateDbContext();
        var bannedUser = await context.Users.AsNoTracking().SingleAsync(
            user => user.Id == scenario.UserIds[1]);
        Assert.True(bannedUser.IsBanned);
        Assert.False(bannedUser.IsAdmin);
    }

    [PostgreSqlFact]
    public async Task Admin_CanRevertOneChange()
    {
        var scenario = await database.CreateScenarioAsync(
            userCount: 2,
            isAdmin: true);
        using var scope = database.Application.Services.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<IAdministrationService>();
        var levels = scope.ServiceProvider.GetRequiredService<ILevelRepository>();

        await levels.PlaceAsync(
            scenario.EventId,
            scenario.UserIds[1],
            1,
            1,
            new PlaceLevelCellRequest(Guid.NewGuid(), "block"));
        await levels.PlaceAsync(
            scenario.EventId,
            scenario.UserIds[1],
            2,
            1,
            new PlaceLevelCellRequest(Guid.NewGuid(), "spike"));
        await levels.PlaceAsync(
            scenario.EventId,
            scenario.UserIds[1],
            1,
            1,
            new PlaceLevelCellRequest(Guid.NewGuid(), "yellow_orb"));

        var reverted = await administration.RevertRevisionAsync(
            scenario.UserIds[0], scenario.EventId, revision: 3);
        var afterRevert = await levels.LoadAsync(scenario.EventId);

        Assert.Equal(1, reverted.ChangedCells);
        Assert.Equal(4, reverted.Revision);
        Assert.Equal("block", afterRevert.Cells.Single(cell => cell.X == 1).Type);

        await using var context = database.CreateDbContext();
        var revisions = await context.PlacementHistory
            .Where(history => history.EventId == scenario.EventId)
            .OrderBy(history => history.Revision)
            .Select(history => history.Revision)
            .ToArrayAsync();
        Assert.Equal(
            new long[] { 1, 2, 3, 4 },
            revisions);
        Assert.All(
            await context.PlacementHistory
                .Where(history => history.EventId == scenario.EventId && history.Revision == 4)
                .ToListAsync(),
            history => Assert.Equal(scenario.UserIds[0], history.UserId));
    }
}
