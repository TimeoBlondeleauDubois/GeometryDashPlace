using DotNetEnv;
using GeometryDashPlace.Web.Auth;
using GeometryDashPlace.Web.Components;
using GeometryDashPlace.Web.Data;
using GeometryDashPlace.Web.Events;
using GeometryDashPlace.Web.Persistence;
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

var connectionString = builder.Environment.IsDevelopment()
    ? $"Host=localhost;Port={dbPort};Username={dbUser};Password={dbPassword};Database={dbName};Include Error Detail=true"
    : $"Host={dbHost};Port={dbPort};Username={dbUser};Password={dbPassword};Database={dbName}";

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContextFactory<GeometryDashPlaceDbContext>(
    options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<ILevelRepository, EntityFrameworkLevelRepository>();
builder.Services.AddScoped<ILevelEventRepository, EntityFrameworkLevelEventRepository>();
builder.Services.AddGoogleAuthentication(builder.Configuration, builder.Environment);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api"),
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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
