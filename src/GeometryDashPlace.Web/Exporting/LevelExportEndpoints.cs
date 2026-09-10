using GeometryDashPlace.Web.Auth;

namespace GeometryDashPlace.Web.Exporting;

public static class LevelExportEndpoints
{
    public static IEndpointRouteBuilder MapLevelExportEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/events/{eventId:guid}/export.gmd", async (
            Guid eventId,
            ILevelExportService exporter,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var export = await exporter.ExportAsync(eventId, cancellationToken);
                return export is null
                    ? Results.NotFound()
                    : Results.File(export.Content, "application/xml", export.FileName);
            }
            catch (LevelExportException exception)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "The level cannot be exported.");
            }
        }).RequireAuthorization(AdminPolicy.Name);

        return endpoints;
    }
}
