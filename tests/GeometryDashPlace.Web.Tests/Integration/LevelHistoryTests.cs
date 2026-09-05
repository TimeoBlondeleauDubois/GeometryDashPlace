using System.Net;
using System.Net.Http.Json;
using GeometryDashPlace.Web.Data.Entities;
using GeometryDashPlace.Web.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeometryDashPlace.Web.Tests.Integration;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class LevelHistoryTests(PostgreSqlIntegrationFixture database)
{
    [PostgreSqlFact]
    public async Task Revisions_ReconstructEveryMutationType()
    {
        var scenario = await database.CreateScenarioAsync();
        using var mutationClient = database.Application.CreateClient(scenario.UserId);
        using var reader = database.Application.CreateClient(userId: null);

        await PutAsync(mutationClient, scenario.EventId, 1, 1, "block");
        await PutAsync(mutationClient, scenario.EventId, 1, 1, "spike");
        await PutAsync(mutationClient, scenario.EventId, 2, 1, "block");
        await MoveAsync(mutationClient, scenario.EventId, 1, 1, 3, 1, "spike");
        await MoveAsync(mutationClient, scenario.EventId, 2, 1, 3, 1, "block");
        await DeleteAsync(mutationClient, scenario.EventId, 3, 1);

        Assert.Empty((await LoadRevisionAsync(reader, scenario.EventId, 0)).Cells);
        AssertLevel(
            await LoadRevisionAsync(reader, scenario.EventId, 1),
            (1, 1, "block"));
        AssertLevel(
            await LoadRevisionAsync(reader, scenario.EventId, 2),
            (1, 1, "spike"));
        AssertLevel(
            await LoadRevisionAsync(reader, scenario.EventId, 3),
            (1, 1, "spike"),
            (2, 1, "block"));
        AssertLevel(
            await LoadRevisionAsync(reader, scenario.EventId, 4),
            (2, 1, "block"),
            (3, 1, "spike"));
        AssertLevel(
            await LoadRevisionAsync(reader, scenario.EventId, 5),
            (3, 1, "block"));
        Assert.Empty((await LoadRevisionAsync(reader, scenario.EventId, 6)).Cells);
    }

    [PostgreSqlFact]
    public async Task NearestSnapshot_ReconstructsAfterOlderHistoryWasArchived()
    {
        var scenario = await database.CreateScenarioAsync();
        using var mutationClient = database.Application.CreateClient(scenario.UserId);
        using var reader = database.Application.CreateClient(userId: null);

        await PutAsync(mutationClient, scenario.EventId, 1, 1, "block");
        await PutAsync(mutationClient, scenario.EventId, 2, 1, "spike");
        await PutAsync(mutationClient, scenario.EventId, 3, 1, "block");
        var revisionThree = await LoadRevisionAsync(reader, scenario.EventId, 3);

        await using (var context = database.CreateDbContext())
        {
            context.LevelSnapshots.Add(new LevelSnapshotEntity
            {
                EventId = scenario.EventId,
                Revision = 3,
                SnapshotType = "manual",
                State = revisionThree.Cells.ToList(),
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
            await context.PlacementHistory
                .Where(history => history.EventId == scenario.EventId &&
                                  history.Revision <= 3)
                .ExecuteDeleteAsync();
        }

        await DeleteAsync(mutationClient, scenario.EventId, 2, 1);

        AssertLevel(
            await LoadRevisionAsync(reader, scenario.EventId, 4),
            (1, 1, "block"),
            (3, 1, "block"));
        using var incomplete = await reader.GetAsync(RevisionUrl(scenario.EventId, 2));
        await AssertProblemAsync(incomplete, HttpStatusCode.Conflict, "history_incomplete");
    }

    [PostgreSqlTheory]
    [InlineData(-1, HttpStatusCode.BadRequest, "invalid_revision")]
    [InlineData(1, HttpStatusCode.NotFound, "revision_not_found")]
    public async Task InvalidRevision_IsRejected(
        long revision,
        HttpStatusCode status,
        string expectedCode)
    {
        var scenario = await database.CreateScenarioAsync();
        using var reader = database.Application.CreateClient(userId: null);

        using var response = await reader.GetAsync(RevisionUrl(scenario.EventId, revision));

        await AssertProblemAsync(response, status, expectedCode);
    }

    private static async Task PutAsync(
        HttpClient client,
        Guid eventId,
        int x,
        int y,
        string type)
    {
        using var response = await client.PutAsJsonAsync(
            $"/api/events/{eventId}/level/cells/{x}/{y}",
            new PlaceLevelCellRequest(Guid.NewGuid(), type));
        response.EnsureSuccessStatusCode();
    }

    private static async Task MoveAsync(
        HttpClient client,
        Guid eventId,
        int sourceX,
        int sourceY,
        int targetX,
        int targetY,
        string type)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/events/{eventId}/level/cells/{sourceX}/{sourceY}/move",
            new MoveLevelCellRequest(Guid.NewGuid(), targetX, targetY, type));
        response.EnsureSuccessStatusCode();
    }

    private static async Task DeleteAsync(
        HttpClient client,
        Guid eventId,
        int x,
        int y)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/events/{eventId}/level/cells/{x}/{y}")
        {
            Content = JsonContent.Create(new DeleteLevelCellRequest(Guid.NewGuid()))
        };
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<LevelState> LoadRevisionAsync(
        HttpClient client,
        Guid eventId,
        long revision)
    {
        using var response = await client.GetAsync(RevisionUrl(eventId, revision));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LevelState>()
            ?? throw new InvalidOperationException("The API returned an empty level state.");
    }

    private static void AssertLevel(
        LevelState state,
        params (int X, int Y, string Type)[] expected)
    {
        Assert.Equal(expected.Length, state.Cells.Count);
        Assert.Equal(
            expected,
            state.Cells.Select(cell => (cell.X, cell.Y, cell.Type)));
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string expectedCode)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(expectedCode, problem.Title);
    }

    private static string RevisionUrl(Guid eventId, long revision) =>
        $"/api/events/{eventId}/level/revisions/{revision}";
}
