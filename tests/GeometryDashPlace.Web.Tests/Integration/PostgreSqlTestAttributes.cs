using System.IO.Pipes;

namespace GeometryDashPlace.Web.Tests.Integration;

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (!PostgreSqlTestEnvironment.IsAvailable)
        {
            Skip = PostgreSqlTestEnvironment.UnavailableReason;
        }
    }
}

public sealed class PostgreSqlTheoryAttribute : TheoryAttribute
{
    public PostgreSqlTheoryAttribute()
    {
        if (!PostgreSqlTestEnvironment.IsAvailable)
        {
            Skip = PostgreSqlTestEnvironment.UnavailableReason;
        }
    }
}

internal static class PostgreSqlTestEnvironment
{
    public const string UnavailableReason =
        "Requires Docker Desktop or the GEOMETRYDASHPLACE_INTEGRATION_TEST_DB environment variable.";

    public static bool IsAvailable { get; } =
        HasExternalDatabase() || HasDockerEndpoint();

    private static bool HasExternalDatabase() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
            PostgreSqlIntegrationFixture.ExternalConnectionStringVariable));

    private static bool HasDockerEndpoint()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")))
        {
            return true;
        }

        if (!OperatingSystem.IsWindows())
        {
            return File.Exists("/var/run/docker.sock");
        }

        try
        {
            using var pipe = new NamedPipeClientStream(
                ".", "docker_engine", PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(250);
            return pipe.IsConnected;
        }
        catch (IOException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
