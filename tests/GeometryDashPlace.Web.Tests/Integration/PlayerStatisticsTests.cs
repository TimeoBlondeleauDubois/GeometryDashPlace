using GeometryDashPlace.Web.Data;
using GeometryDashPlace.Web.Persistence;
using GeometryDashPlace.Web.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GeometryDashPlace.Web.Tests.Integration;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class PlayerStatisticsTests(PostgreSqlIntegrationFixture database)
{
    [PostgreSqlFact]
    public async Task ProfileAndLeaderboard_AggregateRecordedContributions()
    {
        var scenario = await database.CreateScenarioAsync(userCount: 2);
        using var scope = database.Application.Services.CreateScope();
        var levels = scope.ServiceProvider.GetRequiredService<ILevelRepository>();
        var statistics = scope.ServiceProvider.GetRequiredService<IPlayerStatisticsService>();

        await levels.PlaceAsync(
            scenario.EventId,
            scenario.UserIds[0],
            1,
            1,
            new PlaceLevelCellRequest(Guid.NewGuid(), "block"));
        await levels.PlaceAsync(
            scenario.EventId,
            scenario.UserIds[0],
            1,
            1,
            new PlaceLevelCellRequest(Guid.NewGuid(), "spike"));
        await levels.PlaceAsync(
            scenario.EventId,
            scenario.UserIds[1],
            2,
            1,
            new PlaceLevelCellRequest(Guid.NewGuid(), "block"));

        string username;
        string eventSlug;
        await using (var context = database.CreateDbContext())
        {
            username = await context.Users
                .Where(user => user.Id == scenario.UserIds[0])
                .Select(user => user.Username!)
                .SingleAsync();
            eventSlug = await context.Events
                .Where(levelEvent => levelEvent.Id == scenario.EventId)
                .Select(levelEvent => levelEvent.Slug)
                .SingleAsync();
        }

        var profile = await statistics.GetProfileAsync(username.ToLowerInvariant());
        var global = await statistics.GetLeaderboardAsync(null);
        var eventRanking = await statistics.GetLeaderboardAsync(eventSlug);

        Assert.NotNull(profile);
        Assert.Equal(2, profile.Actions.Total);
        Assert.Equal(1, profile.Actions.Placements);
        Assert.Equal(1, profile.Actions.Replacements);
        Assert.Single(profile.Events);
        Assert.Equal(1, profile.Events[0].Rank);
        Assert.Equal(2, profile.RecentActivity.Count);
        Assert.Contains(profile.Badges, badge => badge.Key == "first-step");
        Assert.Equal(1, profile.Progression.Level);
        Assert.Equal(20, profile.Progression.TotalXp);
        Assert.Equal("event-veteran", profile.Progression.NextBadge?.Key);

        Assert.NotNull(global);
        Assert.Equal(scenario.UserIds[0], global.Entries[0].UserId);
        Assert.Equal(profile.Progression.Level, global.Entries[0].Level);
        Assert.Equal(profile.Progression.TotalXp, global.Entries[0].TotalXp);
        Assert.NotNull(eventRanking);
        Assert.Equal(scenario.UserIds[0], eventRanking.Entries[0].UserId);
        Assert.Equal(profile.Progression.Level, eventRanking.Entries[0].Level);
        Assert.Equal(profile.Progression.TotalXp, eventRanking.Entries[0].TotalXp);
        Assert.Equal(eventSlug, eventRanking.SelectedEventSlug);
    }
}
