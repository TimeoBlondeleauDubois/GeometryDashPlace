using GeometryDashPlace.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace GeometryDashPlace.Web.Exporting;

public interface ILevelExportService
{
    Task<LevelExport?> ExportAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);
}

public sealed record LevelExport(string FileName, byte[] Content);

public sealed class LevelExportException(string message) : Exception(message);

public sealed class EntityFrameworkLevelExportService(
    IDbContextFactory<GeometryDashPlaceDbContext> contextFactory,
    IGildPlaceConverter converter) : ILevelExportService
{
    public async Task<LevelExport?> ExportAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var levelEvent = await context.Events
            .AsNoTracking()
            .Where(candidate => candidate.Id == eventId)
            .Select(candidate => new ExportEvent(
                candidate.Slug,
                candidate.Name,
                candidate.Description,
                candidate.BackgroundKey,
                candidate.GroundKey))
            .SingleOrDefaultAsync(cancellationToken);
        if (levelEvent is null)
        {
            return null;
        }

        var cells = await context.LevelCells
            .AsNoTracking()
            .Where(cell => cell.EventId == eventId)
            .OrderBy(cell => cell.X)
            .ThenBy(cell => cell.Y)
            .Select(cell => new ExportCell(
                cell.ObjectTypeKey,
                cell.X,
                cell.Y,
                cell.Rotation,
                cell.ScaleX,
                cell.ScaleY,
                cell.ColorRed,
                cell.ColorGreen,
                cell.ColorBlue,
                cell.DurationSeconds))
            .ToListAsync(cancellationToken);

        var objects = cells.Select(cell => new GildConversionObject(
            cell.Type,
            cell.X,
            cell.Y,
            cell.Rotation,
            cell.ScaleX,
            cell.ScaleY,
            cell.Red,
            cell.Green,
            cell.Blue,
            cell.Duration)).ToArray();
        var content = await converter.ConvertAsync(new GildConversionRequest(
            levelEvent.Name,
            "GeometryDashPlace",
            levelEvent.Description,
            TextureId(levelEvent.BackgroundKey),
            TextureId(levelEvent.GroundKey),
            objects), cancellationToken);
        return new LevelExport($"{levelEvent.Slug}.gmd", content);
    }

    private static int TextureId(string key)
    {
        var separator = key.LastIndexOf('-');
        return separator >= 0 && int.TryParse(key[(separator + 1)..], out var id)
            ? id
            : 1;
    }

    private sealed record ExportEvent(
        string Slug,
        string Name,
        string? Description,
        string BackgroundKey,
        string GroundKey);

    private sealed record ExportCell(
        string Type,
        int X,
        int Y,
        decimal Rotation,
        decimal ScaleX,
        decimal ScaleY,
        short? Red,
        short? Green,
        short? Blue,
        decimal? Duration);
}
