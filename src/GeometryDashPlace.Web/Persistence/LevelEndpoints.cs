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
        level.MapPut("/cells/{x:int}/{y:int}", PlaceAsync);
        level.MapDelete("/cells/{x:int}/{y:int}", DeleteAsync);

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

    private static async Task<IResult> PlaceAsync(
        Guid eventId,
        int x,
        int y,
        [FromBody] PlaceLevelCellRequest request,
        ILevelRepository repository,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await repository.PlaceAsync(eventId, x, y, request, cancellationToken);
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
        ILevelRepository repository,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await repository.DeleteAsync(eventId, x, y, request, cancellationToken);
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
