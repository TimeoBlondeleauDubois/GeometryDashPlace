namespace GeometryDashPlace.Web.Assets;

public sealed record EnvironmentAsset(string Key, string Label, string Path);

public sealed class EnvironmentAssetCatalog
{
    public const string DefaultBackgroundKey = "background-01";
    public const string DefaultGroundKey = "ground-01";

    public EnvironmentAssetCatalog(IWebHostEnvironment environment)
    {
        Backgrounds = Load(
            environment.WebRootPath,
            "backgrounds",
            "/assets/environment/backgrounds");
        Grounds = Load(
            environment.WebRootPath,
            "grounds",
            "/assets/environment/grounds");
    }

    public IReadOnlyList<EnvironmentAsset> Backgrounds { get; }
    public IReadOnlyList<EnvironmentAsset> Grounds { get; }

    public bool HasBackground(string key) =>
        Backgrounds.Any(asset => asset.Key == key);

    public bool HasGround(string key) =>
        Grounds.Any(asset => asset.Key == key);

    public string BackgroundPath(string? key) =>
        Find(Backgrounds, key, DefaultBackgroundKey).Path;

    public string GroundPath(string? key) =>
        Find(Grounds, key, DefaultGroundKey).Path;

    private static EnvironmentAsset Find(
        IReadOnlyList<EnvironmentAsset> assets,
        string? key,
        string defaultKey) =>
        assets.FirstOrDefault(asset => asset.Key == key) ??
        assets.First(asset => asset.Key == defaultKey);

    private static IReadOnlyList<EnvironmentAsset> Load(
        string webRootPath,
        string directory,
        string requestPath)
    {
        var path = Path.Combine(
            webRootPath, "assets", "environment", directory);
        var assets = Directory.GetFiles(path, "*.png")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => new EnvironmentAsset(
                key!,
                key!.Replace('-', ' ').ToUpperInvariant(),
                $"{requestPath}/{key}.png"))
            .OrderBy(asset => asset.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (assets.Length == 0)
        {
            throw new InvalidOperationException(
                $"No PNG asset was found in {path}.");
        }

        return assets;
    }
}
