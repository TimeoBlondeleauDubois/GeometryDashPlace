using GeometryDashPlace.Web.Data;
using GeometryDashPlace.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace GeometryDashPlace.Web.Tests.Integration;

public sealed class PostgreSqlIntegrationFixture : IAsyncLifetime
{
    public const string ExternalConnectionStringVariable =
        "GEOMETRYDASHPLACE_INTEGRATION_TEST_DB";

    private readonly string? _externalConnectionString =
        Environment.GetEnvironmentVariable(ExternalConnectionStringVariable);
    private readonly PostgreSqlContainer? _container;

    public PostgreSqlIntegrationFixture()
    {
        if (!string.IsNullOrWhiteSpace(_externalConnectionString))
        {
            var connection = new NpgsqlConnectionStringBuilder(_externalConnectionString);
            if (!connection.Database.EndsWith("_tests", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The database configured by {ExternalConnectionStringVariable} must end with '_tests'.");
            }
        }
        else
        {
            _container = new PostgreSqlBuilder("postgres:17")
                .WithDatabase("geometry_dash_place_tests")
                .WithUsername("geometrydashplace_tests")
                .WithPassword("geometrydashplace_tests")
                .Build();
        }
    }

    public GeometryDashPlaceApplicationFactory Application { get; private set; } = null!;
    public string ConnectionString => _externalConnectionString ??
        _container?.GetConnectionString() ??
        throw new InvalidOperationException("No integration test database is configured.");

    public async Task InitializeAsync()
    {
        if (_container is not null)
        {
            await _container.StartAsync();

            var scriptDirectory = Path.Combine(AppContext.BaseDirectory, "Sql");
            foreach (var scriptPath in Directory.GetFiles(scriptDirectory, "*.sql").Order())
            {
                var result = await _container.ExecScriptAsync(
                    await File.ReadAllTextAsync(scriptPath));
                if (result.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"PostgreSQL initialization failed for {Path.GetFileName(scriptPath)}: {result.Stderr}");
                }
            }
        }

        Application = new GeometryDashPlaceApplicationFactory(this);
    }

    public async Task DisposeAsync()
    {
        Application?.Dispose();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public GeometryDashPlaceDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<GeometryDashPlaceDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new GeometryDashPlaceDbContext(options);
    }

    public async Task<TestScenario> CreateScenarioAsync(
        int cooldownSeconds = 0,
        bool isBanned = false,
        int userCount = 1,
        string eventStatus = "open")
    {
        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("N");
        var users = Enumerable.Range(0, userCount)
            .Select(index => new UserAccountEntity
            {
                Id = Guid.NewGuid(),
                GoogleSubject = $"integration-{suffix}-{index}",
                Email = $"integration-{suffix}-{index}@example.test",
                DisplayName = $"Integration user {index + 1}",
                IsEmailVerified = true,
                IsBanned = isBanned,
                CreatedAt = now,
                LastLoginAt = now
            })
            .ToList();
        var levelEvent = new LevelEventEntity
        {
            Id = Guid.NewGuid(),
            Slug = $"integration-{suffix}",
            Name = "Integration test event",
            Width = 16,
            Height = 8,
            CooldownSeconds = cooldownSeconds,
            Status = eventStatus,
            StartsAt = now.AddMinutes(-1),
            EndsAt = now.AddHours(1),
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var context = CreateDbContext();
        context.Users.AddRange(users);
        context.Events.Add(levelEvent);
        await context.SaveChangesAsync();
        return new TestScenario(levelEvent.Id, users.Select(user => user.Id).ToArray());
    }
}

public sealed record TestScenario(Guid EventId, IReadOnlyList<Guid> UserIds)
{
    public Guid UserId => UserIds[0];
}
