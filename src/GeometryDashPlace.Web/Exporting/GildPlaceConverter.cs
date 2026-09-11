using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeometryDashPlace.Web.Exporting;

public interface IGildPlaceConverter
{
    Task<byte[]> ConvertAsync(
        GildConversionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record GildConversionRequest(
    string Name,
    string Author,
    string? Description,
    int BackgroundId,
    int GroundId,
    IReadOnlyList<GildConversionObject> Objects);

public sealed record GildConversionObject(
    string Type,
    int X,
    int Y,
    decimal Rotation,
    decimal ScaleX,
    decimal ScaleY,
    short? Red = null,
    short? Green = null,
    short? Blue = null,
    decimal? Duration = null);

public sealed class GildPlaceConverter(
    IWebHostEnvironment environment,
    IConfiguration configuration,
    ILogger<GildPlaceConverter> logger) : IGildPlaceConverter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _converterDirectory = configuration["GILD_PLACE_CONVERTER_PATH"] ??
        Path.Combine(environment.ContentRootPath, "External", "gild_place_converter");
    private readonly string _pythonExecutable = configuration["GILD_PLACE_CONVERTER_PYTHON"] ??
        (OperatingSystem.IsWindows() ? "python" : "python3");

    public async Task<byte[]> ConvertAsync(
        GildConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        var scriptPath = Path.Combine(_converterDirectory, "generate.py");
        var blocksPath = Path.Combine(_converterDirectory, "blocks_list.json");
        if (!File.Exists(scriptPath) || !File.Exists(blocksPath))
        {
            throw new LevelExportException(
                "The Gild level converter submodule is unavailable.");
        }

        var conversionDirectory = Path.Combine(
            Path.GetTempPath(),
            "geometry-dash-place-exports",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(conversionDirectory);
        var inputPath = Path.Combine(conversionDirectory, "level.json");
        var outputPath = Path.Combine(conversionDirectory, "level.gmd");

        try
        {
            await using (var input = File.Create(inputPath))
            {
                await JsonSerializer.SerializeAsync(
                    input,
                    request.Objects,
                    JsonOptions,
                    cancellationToken);
            }

            using var process = new Process
            {
                StartInfo = CreateProcessStartInfo(
                    scriptPath,
                    blocksPath,
                    inputPath,
                    outputPath,
                    request)
            };
            try
            {
                if (!process.Start())
                {
                    throw new LevelExportException(
                        "The Gild level converter could not be started.");
                }
            }
            catch (Exception exception) when (exception is not LevelExportException)
            {
                logger.LogError(exception, "Unable to start the Gild level converter.");
                throw new LevelExportException(
                    "Python is unavailable, so the Gild level converter cannot run.");
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                throw;
            }

            var output = await standardOutput;
            var error = await standardError;
            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                logger.LogError(
                    "Gild level converter failed with exit code {ExitCode}. Output: {Output}. Error: {Error}",
                    process.ExitCode,
                    output,
                    error);
                throw new LevelExportException(
                    "The Gild level converter could not generate this event.");
            }

            return await File.ReadAllBytesAsync(outputPath, cancellationToken);
        }
        finally
        {
            try
            {
                Directory.Delete(conversionDirectory, recursive: true);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Unable to remove export directory {Directory}.", conversionDirectory);
            }
        }
    }

    private ProcessStartInfo CreateProcessStartInfo(
        string scriptPath,
        string blocksPath,
        string inputPath,
        string outputPath,
        GildConversionRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _pythonExecutable,
            WorkingDirectory = _converterDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        AddArgument("--input", inputPath);
        AddArgument("--blocks-list", blocksPath);
        AddArgument("--output", outputPath);
        AddArgument("--name", request.Name);
        AddArgument("--author", request.Author);
        AddArgument("--description", request.Description ?? string.Empty);
        AddArgument("--background", request.BackgroundId.ToString(CultureInfo.InvariantCulture));
        AddArgument("--ground", request.GroundId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Insert(0, scriptPath);
        return startInfo;

        void AddArgument(string name, string value)
        {
            startInfo.ArgumentList.Add(name);
            startInfo.ArgumentList.Add(value);
        }
    }
}
