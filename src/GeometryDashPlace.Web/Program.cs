using DotNetEnv;
using GeometryDashPlace.Web.Administration;
using GeometryDashPlace.Web.Auth;
using GeometryDashPlace.Web.Components;
using GeometryDashPlace.Web.Data;
using GeometryDashPlace.Web.Events;
using GeometryDashPlace.Web.Assets;
using GeometryDashPlace.Web.Persistence;
using GeometryDashPlace.Web.Realtime;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

Env.Load(Path.Combine(builder.Environment.ContentRootPath, ".env"));

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

var dbHost = builder.Configuration["DB_HOST"] ?? "localhost";
var dbPort = builder.Configuration["DB_PORT"] ?? "21556";
var dbUser = builder.Configuration["DB_USERNAME"] ?? "geometrydashplace";
var dbPassword = builder.Configuration["DB_PASSWORD"] ?? "password";
var dbName = builder.Configuration["DB_NAME"] ?? "geometry_dash_place";

builder.Services.AddSingleton(new SiteOwnership(
    builder.Configuration["SITE_OWNER_EMAILS"]));

var connectionString = builder.Environment.IsDevelopment()
    ? $"Host=localhost;Port={dbPort};Username={dbUser};Password={dbPassword};Database={dbName};Include Error Detail=true"
    : $"Host={dbHost};Port={dbPort};Username={dbUser};Password={dbPassword};Database={dbName}";

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSignalR();
builder.Services.AddSingleton<EnvironmentAssetCatalog>();

builder.Services.AddDbContextFactory<GeometryDashPlaceDbContext>(
    options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<ILevelRepository, EntityFrameworkLevelRepository>();
builder.Services.AddScoped<ILevelEventRepository, EntityFrameworkLevelEventRepository>();
builder.Services.AddScoped<IEventResultsRepository, EntityFrameworkEventResultsRepository>();
builder.Services.AddScoped<EntityFrameworkAdministrationService>();
builder.Services.AddScoped<IAdministrationService>(services =>
    services.GetRequiredService<EntityFrameworkAdministrationService>());
builder.Services.AddScoped<IEventLifecycleService>(services =>
    services.GetRequiredService<EntityFrameworkAdministrationService>());
builder.Services.AddHostedService<EventLifecycleWorker>();
builder.Services.AddSingleton<PlacementPreviewPresence>();
builder.Services.AddSingleton<LevelRealtimeService>();
builder.Services.AddHostedService<PlacementPreviewCleanupWorker>();
builder.Services.AddScoped<EditorCircuitPresence>();
builder.Services.AddScoped<CircuitHandler>(services =>
    services.GetRequiredService<EditorCircuitPresence>());
builder.Services.AddSingleton<EventLifecycleNotifier>();
builder.Services.AddGoogleAuthentication(builder.Configuration, builder.Environment);

if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo("/var/geometrydashplace/keys"))
        .SetApplicationName("GeometryDashPlace.Web");
}

var app = builder.Build();

var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedHost |
                       ForwardedHeaders.XForwardedProto
};
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api") &&
               !context.Request.Path.StartsWithSegments("/hubs"),
    branch => branch.UseStatusCodePagesWithReExecute(
        "/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapGoogleAuthEndpoints();
app.MapLevelEventEndpoints();
app.MapLevelEndpoints();
app.MapHub<LevelHub>("/hubs/level");
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
