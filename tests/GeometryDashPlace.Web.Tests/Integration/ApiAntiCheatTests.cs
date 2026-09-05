using System.Net;
using System.Net.Http.Json;
using GeometryDashPlace.Web.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeometryDashPlace.Web.Tests.Integration;

public sealed class ApiAntiCheatTests(
    PostgreSqlIntegrationFixture database) : IClassFixture<PostgreSqlIntegrationFixture>
{
    public static TheoryData<int, int, PlaceLevelCellRequest, string> InvalidPlacements => new()
    {
        {
            -1, 0,
            new PlaceLevelCellRequest(Guid.NewGuid(), "block"),
            "cell_out_of_bounds"
        },
        {
            0, 0,
            new PlaceLevelCellRequest(Guid.NewGuid(), "unknown_object"),
            "object_type_not_found"
        },
        {
            0, 0,
            new PlaceLevelCellRequest(Guid.NewGuid(), "block", Rotation: 45),
            "unsupported_rotation"
        },
        {
            0, 0,
            new PlaceLevelCellRequest(Guid.NewGuid(), "spike", ScaleX: 2.01),
            "invalid_scale"
        },
        {
            0, 0,
            new PlaceLevelCellRequest(Guid.NewGuid(), "block", Red: 255),
            "invalid_color"
        },
        {
            0, 0,
            new PlaceLevelCellRequest(
                Guid.NewGuid(), "bg_color_trigger",
                Red: 30, Green: 35, Blue: 205),
            "invalid_duration"
        }
    };

    [Fact]
    public async Task AnonymousMutation_IsRejectedBeforeDatabaseChanges()
    {
        var scenario = await database.CreateScenarioAsync();
        using var client = database.Application.CreateClient(userId: null);

        var response = await client.PutAsJsonAsync(
            CellUrl(scenario.EventId, 1, 1),
            new PlaceLevelCellRequest(Guid.NewGuid(), "block"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertEmptyLevelAsync(scenario.EventId);
    }

    [Fact]
    public async Task BannedUser_CannotReuseAnExistingSessionToPlace()
    {
        var scenario = await database.CreateScenarioAsync(isBanned: true);
        using var client = database.Application.CreateClient(scenario.UserId);

        var response = await client.PutAsJsonAsync(
            CellUrl(scenario.EventId, 1, 1),
            new PlaceLevelCellRequest(Guid.NewGuid(), "block"));

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "user_banned");
        await AssertEmptyLevelAsync(scenario.EventId);
    }

    [Theory]
    [MemberData(nameof(InvalidPlacements))]
    public async Task ForgedInvalidPlacement_IsRejectedWithoutConsumingTheTurn(
        int x,
        int y,
        PlaceLevelCellRequest request,
        string expectedCode)
    {
        var scenario = await database.CreateScenarioAsync(cooldownSeconds: 60);
        using var client = database.Application.CreateClient(scenario.UserId);

        var response = await client.PutAsJsonAsync(
            CellUrl(scenario.EventId, x, y), request);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, expectedCode);
        await AssertEmptyLevelAsync(scenario.EventId);
    }

    [Fact]
    public async Task ClosedEvent_CannotBeModified()
    {
        var scenario = await database.CreateScenarioAsync(eventStatus: "closed");
        using var client = database.Application.CreateClient(scenario.UserId);

        var response = await client.PutAsJsonAsync(
            CellUrl(scenario.EventId, 1, 1),
            new PlaceLevelCellRequest(Guid.NewGuid(), "block"));

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "event_not_open");
        await AssertEmptyLevelAsync(scenario.EventId);
    }

    [Fact]
    public async Task Cooldown_CannotBeBypassedWithAnotherRequest()
    {
        var scenario = await database.CreateScenarioAsync(cooldownSeconds: 60);
        using var client = database.Application.CreateClient(scenario.UserId);

        var first = await client.PutAsJsonAsync(
            CellUrl(scenario.EventId, 1, 1),
            new PlaceLevelCellRequest(Guid.NewGuid(), "block"));
        var second = await client.PutAsJsonAsync(
            CellUrl(scenario.EventId, 2, 1),
            new PlaceLevelCellRequest(Guid.NewGuid(), "spike"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var problem = await AssertProblemAsync(
            second, HttpStatusCode.TooManyRequests, "placement_cooldown");
        Assert.True(problem.Extensions.ContainsKey("retryAt"));

        await using var context = database.CreateDbContext();
        Assert.Equal(1, await context.LevelCells.CountAsync(
            cell => cell.EventId == scenario.EventId));
        Assert.Equal(1, await context.PlacementHistory.CountAsync(
            history => history.EventId == scenario.EventId));
        var state = await context.UserEventStates.SingleAsync(
            candidate => candidate.EventId == scenario.EventId &&
                         candidate.UserId == scenario.UserId);
        Assert.Equal(1, state.PlacementCount);
    }

    [Fact]
    public async Task IdenticalRequestReplay_IsIdempotentDuringCooldown()
    {
        var scenario = await database.CreateScenarioAsync(cooldownSeconds: 60);
        using var client = database.Application.CreateClient(scenario.UserId);
        var request = new PlaceLevelCellRequest(Guid.NewGuid(), "block", Rotation: 90);
        var url = CellUrl(scenario.EventId, 1, 1);

        var firstResponse = await client.PutAsJsonAsync(url, request);
        var replayResponse = await client.PutAsJsonAsync(url, request);
        var first = await ReadSuccessAsync(firstResponse);
        var replay = await ReadSuccessAsync(replayResponse);

        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Revision, replay.Revision);

        await using var context = database.CreateDbContext();
        Assert.Equal(1, await context.LevelCells.CountAsync(
            cell => cell.EventId == scenario.EventId));
        Assert.Equal(1, await context.PlacementHistory.CountAsync(
            history => history.EventId == scenario.EventId));
        Assert.Equal(1, (await context.UserEventStates.SingleAsync(
            state => state.EventId == scenario.EventId &&
                     state.UserId == scenario.UserId)).PlacementCount);
    }

    [Fact]
    public async Task ReusedRequestId_ForDifferentMutationIsRejected()
    {
        var scenario = await database.CreateScenarioAsync();
        using var client = database.Application.CreateClient(scenario.UserId);
        var requestId = Guid.NewGuid();

        var first = await client.PutAsJsonAsync(
            CellUrl(scenario.EventId, 1, 1),
            new PlaceLevelCellRequest(requestId, "block"));
        var conflict = await client.PutAsJsonAsync(
            CellUrl(scenario.EventId, 2, 1),
            new PlaceLevelCellRequest(requestId, "spike"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await AssertProblemAsync(
            conflict, HttpStatusCode.Conflict, "request_id_conflict");

        await using var context = database.CreateDbContext();
        Assert.Equal(1, await context.LevelCells.CountAsync(
            cell => cell.EventId == scenario.EventId));
        Assert.Equal(1, await context.PlacementHistory.CountAsync(
            history => history.EventId == scenario.EventId));
    }

    [Fact]
    public async Task ConcurrentPlacements_ConvergeToUniqueAtomicRevisions()
    {
        const int placementCount = 6;
        var scenario = await database.CreateScenarioAsync(
            cooldownSeconds: 0, userCount: placementCount);
        var requests = scenario.UserIds
            .Select((userId, index) => new ConcurrentPlacement(
                database.Application.CreateClient(userId),
                CellUrl(scenario.EventId, index, 1),
                new PlaceLevelCellRequest(Guid.NewGuid(), "spike")))
            .ToArray();

        try
        {
            var mutations = await Task.WhenAll(
                requests.Select(PlaceWithConcurrencyRetryAsync));

            Assert.All(mutations, mutation => Assert.False(mutation.IsReplay));
            await using var context = database.CreateDbContext();
            var revisions = await context.PlacementHistory
                .Where(history => history.EventId == scenario.EventId)
                .OrderBy(history => history.Revision)
                .Select(history => history.Revision)
                .ToArrayAsync();
            Assert.Equal(
                Enumerable.Range(1, placementCount).Select(value => (long)value),
                revisions);
            Assert.Equal(placementCount, await context.LevelCells.CountAsync(
                cell => cell.EventId == scenario.EventId));
            Assert.Equal(placementCount, (await context.Events.SingleAsync(
                levelEvent => levelEvent.Id == scenario.EventId)).CurrentRevision);
        }
        finally
        {
            foreach (var request in requests)
            {
                request.Client.Dispose();
            }
        }
    }

    private async Task AssertEmptyLevelAsync(Guid eventId)
    {
        await using var context = database.CreateDbContext();
        Assert.Equal(0, (await context.Events.SingleAsync(
            levelEvent => levelEvent.Id == eventId)).CurrentRevision);
        Assert.False(await context.LevelCells.AnyAsync(cell => cell.EventId == eventId));
        Assert.False(await context.PlacementHistory.AnyAsync(history => history.EventId == eventId));
        Assert.False(await context.UserEventStates.AnyAsync(state => state.EventId == eventId));
    }

    private static async Task<ProblemDetails> AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(expectedCode, problem.Title);
        return problem;
    }

    private static async Task<LevelMutation> ReadSuccessAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LevelMutation>()
            ?? throw new InvalidOperationException("The API returned an empty mutation.");
    }

    private static async Task<LevelMutation> PlaceWithConcurrencyRetryAsync(
        ConcurrentPlacement placement)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var response = await placement.Client.PutAsJsonAsync(
                placement.Url, placement.Request);
            if (response.IsSuccessStatusCode)
            {
                return await ReadSuccessAsync(response);
            }

            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            if (response.StatusCode != HttpStatusCode.Conflict ||
                problem?.Title != "concurrent_update")
            {
                throw new InvalidOperationException(
                    $"Unexpected concurrency response: {(int)response.StatusCode} {problem?.Title}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(15 * (attempt + 1)));
        }

        throw new TimeoutException("The placement never succeeded after concurrency retries.");
    }

    private static string CellUrl(Guid eventId, int x, int y) =>
        $"/api/events/{eventId}/level/cells/{x}/{y}";

    private sealed record ConcurrentPlacement(
        HttpClient Client,
        string Url,
        PlaceLevelCellRequest Request);
}
