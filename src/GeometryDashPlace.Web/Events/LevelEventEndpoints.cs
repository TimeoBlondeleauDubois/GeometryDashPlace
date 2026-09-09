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

        endpoints.MapGet("/api/events", async (
            ILevelEventRepository repository,
            CancellationToken cancellationToken) =>
            Results.Ok(await repository.GetPastAsync(cancellationToken)));

        endpoints.MapGet("/api/events/{slug}", async (
            string slug,
            ILevelEventRepository repository,
            CancellationToken cancellationToken) =>
        {
            var levelEvent = await repository.GetBySlugAsync(slug, cancellationToken);
            return levelEvent is null ? Results.NotFound() : Results.Ok(levelEvent);
        });

        endpoints.MapGet("/api/events/{slug}/results", async (
            string slug,
            IEventResultsRepository repository,
            CancellationToken cancellationToken) =>
        {
            var results = await repository.GetBySlugAsync(slug, cancellationToken);
            return results is null ? Results.NotFound() : Results.Ok(results);
        });

        return endpoints;
    }
}
