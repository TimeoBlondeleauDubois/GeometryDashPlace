using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using GeometryDashPlace.Web.Exporting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeometryDashPlace.Web.Tests;

public sealed class GildPlaceConverterTests
{
    [GildConverterFact]
    public async Task ConvertAsync_RunsTheExternalRepositoryScript()
    {
        var python = Environment.GetEnvironmentVariable("GILD_PLACE_CONVERTER_TEST_PYTHON")!;
        var converterPath = Environment.GetEnvironmentVariable("GILD_PLACE_CONVERTER_TEST_PATH")!;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GILD_PLACE_CONVERTER_PYTHON"] = python,
                ["GILD_PLACE_CONVERTER_PATH"] = converterPath
            })
            .Build();
        var converter = new GildPlaceConverter(
            new TestEnvironment(converterPath),
            configuration,
            NullLogger<GildPlaceConverter>.Instance);

        var content = await converter.ConvertAsync(new GildConversionRequest(
            "External converter",
            "GeometryDashPlace",
            "Integration test",
            3,
            7,
            [new GildConversionObject("block", 4, 0, 45, 1.5m, 1)]));

        var document = XDocument.Parse(Encoding.UTF8.GetString(content));
        Assert.Equal("External converter", PlistValue(document, "k2"));
        var levelData = DecodeLevelData(PlistValue(document, "k4"));
        Assert.StartsWith("kA6,3,kA7,7,kA32,0;", levelData);
        Assert.Contains("1,1,2,135.0,3,15.0,6,45,128,1.5,129,1;", levelData);
    }

    private static string PlistValue(XDocument document, string key)
    {
        var elements = document.Root!.Element("dict")!.Elements().ToArray();
        var keyIndex = Array.FindIndex(
            elements,
            element => element.Name.LocalName == "k" && element.Value == key);
        Assert.True(keyIndex >= 0 && keyIndex + 1 < elements.Length);
        return elements[keyIndex + 1].Value;
    }

    private static string DecodeLevelData(string encoded)
    {
        var compressed = Convert.FromBase64String(
            encoded.Replace('-', '+').Replace('_', '/'));
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private sealed class TestEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "GeometryDashPlace.Web.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

public sealed class GildConverterFactAttribute : FactAttribute
{
    public GildConverterFactAttribute()
    {
        var python = Environment.GetEnvironmentVariable("GILD_PLACE_CONVERTER_TEST_PYTHON");
        var converterPath = Environment.GetEnvironmentVariable("GILD_PLACE_CONVERTER_TEST_PATH");
        if (string.IsNullOrWhiteSpace(python) ||
            string.IsNullOrWhiteSpace(converterPath) ||
            !File.Exists(python) ||
            !File.Exists(Path.Combine(converterPath, "generate.py")))
        {
            Skip = "Requires a Python executable and the Gild converter test path.";
        }
    }
}
