using System.Net.Http.Json;
using GeometryDashPlace.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeometryDashPlace.Web.Tests.Integration;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class LevelSnapshotTests(PostgreSqlIntegrationFixture database)
{
    [PostgreSqlFact]
    public async Task FirstSuccessfulMutation_CreatesSnapshotOfCommittedState()
    {
        var scenario = await database.CreateScenarioAsync();
        using var client = database.Application.CreateClient(scenario.UserId);

        var response = await client.PutAsJsonAsync(
            CellUrl(scenario.EventId, 2, 3),
            new PlaceLevelCellRequest(Guid.NewGuid(), "spike", Rotation: 90));

        await AssertSuccessAsync(response);
        await using var context = database.CreateDbContext();
        var snapshot = await context.LevelSnapshots
            .SingleAsync(candidate => candidate.EventId == scenario.EventId);
        var cell = Assert.Single(snapshot.State);
        Assert.Equal(1, snapshot.Revision);
        Assert.Equal("hourly", snapshot.SnapshotType);
        Assert.Equal((2, 3), (cell.X, cell.Y));
        Assert.Equal("spike", cell.Type);
        Assert.Equal(90, cell.Rotation);
        Assert.Equal(scenario.UserId, cell.AuthorUserId);
    }

    [PostgreSqlFact]
    public async Task MutationsWithinOneHour_DoNotCreateDuplicateSnapshots()
    {
        var scenario = await database.CreateScenarioAsync();
        using var client = database.Application.CreateClient(scenario.UserId);

        await AssertSuccessAsync(await client.PutAsJsonAsync(
            CellUrl(scenario.EventId, 1, 1),
            new PlaceLevelCellRequest(Guid.NewGuid(), "block")));
        await AssertSuccessAsync(await client.PutAsJsonAsync(
            CellUrl(scenario.EventId, 2, 1),
            new PlaceLevelCellRequest(Guid.NewGuid(), "spike")));

        await using var context = database.CreateDbContext();
        var snapshot = await context.LevelSnapshots
            .SingleAsync(candidate => candidate.EventId == scenario.EventId);
        Assert.Equal(1, snapshot.Revision);
    }

    [PostgreSqlFact]
    public async Task MutationAfterOneHour_CreatesCurrentFullStateSnapshot()
    {
        var scenario = await database.CreateScenarioAsync();
        using var client = database.Application.CreateClient(scenario.UserId);

        await AssertSuccessAsync(await client.PutAsJsonAsync(
            CellUrl(scenario.EventId, 1, 1),
            new PlaceLevelCellRequest(Guid.NewGuid(), "block")));

        await using (var context = database.CreateDbContext())
        {
            var levelEvent = await context.Events.SingleAsync(
                candidate => candidate.Id == scenario.EventId);
            levelEvent.LastSnapshotAt = DateTimeOffset.UtcNow.AddHours(-2);
            await context.SaveChangesAsync();
        }

        await AssertSuccessAsync(await client.PutAsJsonAsync(
            CellUrl(scenario.EventId, 2, 1),
            new PlaceLevelCellRequest(Guid.NewGuid(), "spike")));

        await using var verification = database.CreateDbContext();
        var snapshots = await verification.LevelSnapshots
            .Where(snapshot => snapshot.EventId == scenario.EventId)
            .OrderBy(snapshot => snapshot.Revision)
            .ToListAsync();
        Assert.Equal(2, snapshots.Count);
        Assert.Equal(2, snapshots[1].Revision);
        Assert.Collection(
            snapshots[1].State,
            cell => Assert.Equal((1, 1, "block"), (cell.X, cell.Y, cell.Type)),
            cell => Assert.Equal((2, 1, "spike"), (cell.X, cell.Y, cell.Type)));
    }

    private static string CellUrl(Guid eventId, int x, int y) =>
        $"/api/events/{eventId}/level/cells/{x}/{y}";

    private static async Task AssertSuccessAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.True(
                response.IsSuccessStatusCode,
                $"Unexpected {(int)response.StatusCode} response: " +
                await response.Content.ReadAsStringAsync());
        }
    }
}
