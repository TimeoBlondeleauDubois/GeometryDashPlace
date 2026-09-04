using System.Security.Claims;
using GeometryDashPlace.Web.Auth;
using GeometryDashPlace.Web.Realtime;
using Microsoft.AspNetCore.Mvc;

namespace GeometryDashPlace.Web.Persistence;

public static class LevelEndpoints
{
    public static IEndpointRouteBuilder MapLevelEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var level = endpoints
            .MapGroup("/api/events/{eventId:guid}/level")
            .WithTags("Level");

        level.MapGet("/", LoadAsync);
        level.MapGet("/cooldown", GetCooldownAsync).RequireAuthorization();
        level.MapPut("/cells/{x:int}/{y:int}", PlaceAsync).RequireAuthorization();
        level.MapDelete("/cells/{x:int}/{y:int}", DeleteAsync).RequireAuthorization();
        level.MapPost("/cells/{sourceX:int}/{sourceY:int}/move", MoveAsync).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> LoadAsync(
        Guid eventId,
        ILevelRepository repository,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await repository.LoadAsync(eventId, cancellationToken));
        }
        catch (LevelPersistenceException exception)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> GetCooldownAsync(
        Guid eventId,
        ClaimsPrincipal principal,
        ILevelRepository repository,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!AuthenticatedUser.TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await repository.GetCooldownAsync(
                eventId, userId, cancellationToken));
        }
        catch (LevelPersistenceException exception)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> PlaceAsync(
        Guid eventId,
        int x,
        int y,
        [FromBody] PlaceLevelCellRequest request,
        ClaimsPrincipal principal,
        ILevelRepository repository,
        LevelRealtimeService realtime,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!AuthenticatedUser.TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await repository.PlaceAsync(
                eventId, userId, x, y, request, cancellationToken);
            if (!result.IsReplay)
            {
                await realtime.PublishAsync(new LevelChange(
                    eventId, userId, result.Action, result.Revision,
                    x, y, null, null, result.NextPlacementAt, result.Cell));
            }
            return Results.Ok(result);
        }
        catch (LevelPersistenceException exception)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> DeleteAsync(
        Guid eventId,
        int x,
        int y,
        [FromBody] DeleteLevelCellRequest request,
        ClaimsPrincipal principal,
        ILevelRepository repository,
        LevelRealtimeService realtime,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!AuthenticatedUser.TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await repository.DeleteAsync(
                eventId, userId, x, y, request, cancellationToken);
            if (!result.IsReplay)
            {
                await realtime.PublishAsync(new LevelChange(
                    eventId, userId, result.Action, result.Revision,
                    x, y, null, null, result.NextPlacementAt, null));
            }
            return Results.Ok(result);
        }
        catch (LevelPersistenceException exception)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<IResult> MoveAsync(
        Guid eventId,
        int sourceX,
        int sourceY,
        [FromBody] MoveLevelCellRequest request,
        ClaimsPrincipal principal,
        ILevelRepository repository,
        LevelRealtimeService realtime,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!AuthenticatedUser.TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await repository.MoveAsync(
                eventId, userId, sourceX, sourceY, request, cancellationToken);
            if (!result.IsReplay)
            {
                await realtime.PublishAsync(new LevelChange(
                    eventId, userId, result.Action, result.Revision,
                    request.TargetX, request.TargetY, sourceX, sourceY,
                    result.NextPlacementAt, result.Cell));
            }
            return Results.Ok(result);
        }
        catch (LevelPersistenceException exception)
        {
            return ToProblem(exception);
        }
    }

    private static IResult ToProblem(LevelPersistenceException exception)
    {
        Dictionary<string, object?>? extensions = null;
        if (exception.RetryAt is not null)
        {
            extensions = new Dictionary<string, object?>
            {
                ["retryAt"] = exception.RetryAt
            };
        }

        return Results.Problem(
            detail: exception.Message,
            statusCode: exception.StatusCode,
            title: exception.Code,
            extensions: extensions);
    }
}
