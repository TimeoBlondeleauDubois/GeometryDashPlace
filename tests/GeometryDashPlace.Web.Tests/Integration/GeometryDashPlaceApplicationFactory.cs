using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using GeometryDashPlace.Web.Auth;
using GeometryDashPlace.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeometryDashPlace.Web.Tests.Integration;

public sealed class GeometryDashPlaceApplicationFactory(
    PostgreSqlIntegrationFixture database) : WebApplicationFactory<Program>
{
    public HttpClient CreateClient(Guid? userId)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        if (userId is { } authenticatedUserId)
        {
            client.DefaultRequestHeaders.Add(
                TestAuthenticationHandler.UserIdHeader,
                authenticatedUserId.ToString());
        }

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("GOOGLE_CLIENT_ID", "integration-test-client");
        builder.UseSetting("GOOGLE_CLIENT_SECRET", "integration-test-secret");
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDbContextFactory<GeometryDashPlaceDbContext>>();
            services.RemoveAll<DbContextOptions<GeometryDashPlaceDbContext>>();
            services.AddDbContextFactory<GeometryDashPlaceDbContext>(
                options => options.UseNpgsql(database.ConnectionString));

            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName, _ => { });
        });
    }
}

internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "IntegrationTests";
    public const string UserIdHeader = "X-Integration-User-Id";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserIdHeader, out var values) ||
            !Guid.TryParse(values.SingleOrDefault(), out var userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
        [
            new Claim(AuthenticatedUser.UserIdClaim, userId.ToString()),
            new Claim(ClaimTypes.Name, "Integration test user")
        ], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = (int)HttpStatusCode.Unauthorized;
        return Task.CompletedTask;
    }
}
