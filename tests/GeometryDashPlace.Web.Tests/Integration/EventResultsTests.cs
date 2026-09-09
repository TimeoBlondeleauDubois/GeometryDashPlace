using GeometryDashPlace.Web.Events;
using GeometryDashPlace.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GeometryDashPlace.Web.Tests.Integration;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class EventResultsTests(PostgreSqlIntegrationFixture database)
{
    [PostgreSqlFact]
    public async Task CompletedEvent_ExposesFinalLevelAndContributorStatistics()
    {
        var scenario = await database.CreateScenarioAsync(userCount: 2);
        using var scope = database.Application.Services.CreateScope();
        var levels = scope.ServiceProvider.GetRequiredService<ILevelRepository>();
        var resultsRepository = scope.ServiceProvider.GetRequiredService<IEventResultsRepository>();

        await levels.PlaceAsync(
            scenario.EventId,
            scenario.UserIds[0],
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
            scenario.UserIds[0],
            1,
            1,
            new PlaceLevelCellRequest(Guid.NewGuid(), "yellow_orb"));

        string slug;
        await using (var context = database.CreateDbContext())
        {
            var levelEvent = await context.Events.SingleAsync(
                candidate => candidate.Id == scenario.EventId);
            levelEvent.Status = "closed";
            levelEvent.EndsAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            slug = levelEvent.Slug;
            await context.SaveChangesAsync();
        }

        var results = await resultsRepository.GetBySlugAsync(slug);

        Assert.NotNull(results);
        Assert.Equal(2, results.FinalObjectCount);
        Assert.Equal(3, results.Actions.Total);
        Assert.Equal(2, results.Actions.Placements);
        Assert.Equal(1, results.Actions.Replacements);
        Assert.Equal(2, results.Contributors.Count);
        Assert.Equal(2, results.Contributors[0].Actions.Total);
        Assert.NotNull(results.FirstContributionAt);
        Assert.NotNull(results.LastContributionAt);
    }

    [PostgreSqlFact]
    public async Task OngoingEvent_DoesNotExposeResults()
    {
        var scenario = await database.CreateScenarioAsync();
        using var scope = database.Application.Services.CreateScope();
        var resultsRepository = scope.ServiceProvider.GetRequiredService<IEventResultsRepository>();
        await using var context = database.CreateDbContext();
        var slug = await context.Events
            .Where(levelEvent => levelEvent.Id == scenario.EventId)
            .Select(levelEvent => levelEvent.Slug)
            .SingleAsync();

        Assert.Null(await resultsRepository.GetBySlugAsync(slug));
    }
}
