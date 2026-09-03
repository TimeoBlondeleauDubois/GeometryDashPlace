namespace GeometryDashPlace.Web.Events;

public static class LevelEventEndpoints
{
    public static IEndpointRouteBuilder MapLevelEventEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/events/current", async (
            ILevelEventRepository repository,
            CancellationToken cancellationToken) =>
        {
            var currentEvent = await repository.GetCurrentAsync(cancellationToken);
            return currentEvent is null ? Results.NotFound() : Results.Ok(currentEvent);
        });

        return endpoints;
    }
}
