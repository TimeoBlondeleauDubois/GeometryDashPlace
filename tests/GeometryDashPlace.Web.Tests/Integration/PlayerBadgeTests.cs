using GeometryDashPlace.Web.Data.Entities;
using GeometryDashPlace.Web.Persistence;
using GeometryDashPlace.Web.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GeometryDashPlace.Web.Tests.Integration;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class PlayerBadgeTests(PostgreSqlIntegrationFixture database)
{
    [PostgreSqlFact]
    public async Task ContributionBadge_IsPersistedAndNotifiedOnlyOnce()
    {
        var scenario = await database.CreateScenarioAsync();
        using var scope = database.Application.Services.CreateScope();
        var levels = scope.ServiceProvider.GetRequiredService<ILevelRepository>();
        var badges = scope.ServiceProvider.GetRequiredService<IPlayerBadgeService>();

        await levels.PlaceAsync(
            scenario.EventId,
            scenario.UserId,
            1,
            1,
            new PlaceLevelCellRequest(Guid.NewGuid(), "block"));

        var pending = await badges.GetPendingAsync(scenario.UserId);
        var firstStep = Assert.Single(pending);
        Assert.Equal("first-step", firstStep.Key);

        await using (var context = database.CreateDbContext())
        {
            Assert.Equal(
                1,
                await context.PlayerBadges.CountAsync(badge =>
                    badge.UserId == scenario.UserId &&
                    badge.BadgeKey == "first-step"));
        }

        await badges.MarkSeenAsync(
            scenario.UserId,
            [new PlayerBadgeReference(firstStep.Key, firstStep.ScopeKey)]);

        Assert.Empty(await badges.GetPendingAsync(scenario.UserId));
    }

    [PostgreSqlFact]
    public async Task CompletedEvent_AwardsTopThreeExactlyOnce()
    {
        var scenario = await database.CreateScenarioAsync(userCount: 4);
        using var scope = database.Application.Services.CreateScope();
        var levels = scope.ServiceProvider.GetRequiredService<ILevelRepository>();
        var badges = scope.ServiceProvider.GetRequiredService<IPlayerBadgeService>();

        for (var index = 0; index < scenario.UserIds.Count; index++)
        {
            await levels.PlaceAsync(
                scenario.EventId,
                scenario.UserIds[index],
                index,
                1,
                new PlaceLevelCellRequest(Guid.NewGuid(), "block"));
        }

        await using (var context = database.CreateDbContext())
        {
            var levelEvent = await context.Events.SingleAsync(candidate =>
                candidate.Id == scenario.EventId);
            levelEvent.Status = "closed";
            levelEvent.EndsAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync();
        }

        Assert.Equal(3, await badges.AwardCompletedEventBadgesAsync());
        Assert.Equal(0, await badges.AwardCompletedEventBadgesAsync());

        await using (var context = database.CreateDbContext())
        {
            var awarded = await context.PlayerBadges
                .Where(badge =>
                    badge.EventId == scenario.EventId &&
                    badge.BadgeKey == "event-top-three")
                .ToListAsync();
            Assert.Equal(3, awarded.Count);
            Assert.All(awarded, badge => Assert.Null(badge.SeenAt));
        }
    }
}
