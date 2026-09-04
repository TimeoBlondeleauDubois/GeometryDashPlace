using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;

namespace GeometryDashPlace.Web.Auth;

public static class GoogleAuthExtensions
{
    private const string GoogleScheme = "Google";

    public static IServiceCollection AddGoogleAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var clientId = FirstNonEmpty(configuration,
            "Authentication:Google:ClientId",
            "AUTHENTICATION__GOOGLE__CLIENTID",
            "GOOGLE_CLIENT_ID");
        var clientSecret = FirstNonEmpty(configuration,
            "Authentication:Google:ClientSecret",
            "AUTHENTICATION__GOOGLE__CLIENTSECRET",
            "GOOGLE_CLIENT_SECRET");
        var callbackPath = FirstNonEmpty(configuration,
            "Authentication:Google:CallbackPath",
            "AUTHENTICATION__GOOGLE__CALLBACKPATH") ?? "/signin-google";

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "Google OAuth is not configured. Set GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET.");
        }

        services.AddScoped<GoogleUserSynchronizer>();
        services.AddCascadingAuthenticationState();
        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.LoginPath = "/login/google";
                options.LogoutPath = "/logout";
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;
                options.Cookie.IsEssential = true;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.Events.OnRedirectToLogin = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api") ||
                        context.Request.Path.StartsWithSegments("/hubs"))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    }
                    else
                    {
                        context.Response.Redirect(context.RedirectUri);
                    }

                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api") ||
                        context.Request.Path.StartsWithSegments("/hubs"))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    }
                    else
                    {
                        context.Response.Redirect(context.RedirectUri);
                    }

                    return Task.CompletedTask;
                };
            })
            .AddOAuth(GoogleScheme, options =>
            {
                options.ClientId = clientId;
                options.ClientSecret = clientSecret;
                options.CallbackPath = callbackPath;
                options.AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
                options.TokenEndpoint = "https://oauth2.googleapis.com/token";
                options.UserInformationEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";
                options.UsePkce = true;
                options.SaveTokens = false;
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "sub");
                options.ClaimActions.MapJsonKey(ClaimTypes.Name, "name");
                options.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");
                options.ClaimActions.MapJsonKey("google:picture", "picture");

                options.Events = new OAuthEvents
                {
                    OnCreatingTicket = CreateGoogleTicketAsync,
                    OnRemoteFailure = context =>
                    {
                        context.HandleResponse();
                        context.Response.Redirect("/?authError=google");
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();
        return services;
    }

    public static IEndpointRouteBuilder MapGoogleAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/login/google", async (HttpContext context, string? returnUrl) =>
        {
            var redirectUri = IsLocalReturnUrl(returnUrl) ? returnUrl! : "/";
            await context.ChallengeAsync(
                GoogleScheme,
                new AuthenticationProperties { RedirectUri = redirectUri });
        }).AllowAnonymous();

        endpoints.MapGet("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Redirect("/");
        }).RequireAuthorization();

        return endpoints;
    }

    private static async Task CreateGoogleTicketAsync(OAuthCreatingTicketContext context)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        using var response = await context.Backchannel.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, context.HttpContext.RequestAborted);
        response.EnsureSuccessStatusCode();

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(
            context.HttpContext.RequestAborted));
        var user = payload.RootElement;
        context.RunClaimActions(user);

        var subject = RequiredString(user, "sub");
        var email = RequiredString(user, "email");
        var displayName = RequiredString(user, "name");
        var emailVerified = user.TryGetProperty("email_verified", out var verified) && verified.GetBoolean();
        var avatarUrl = user.TryGetProperty("picture", out var picture) ? picture.GetString() : null;
        if (!emailVerified)
        {
            context.Fail("A verified Google email address is required.");
            return;
        }

        var synchronizedUser = await context.HttpContext.RequestServices
            .GetRequiredService<GoogleUserSynchronizer>()
            .SynchronizeAsync(
                subject,
                email,
                displayName[..Math.Min(displayName.Length, 100)],
                avatarUrl,
                emailVerified,
                context.HttpContext.RequestAborted);
        if (synchronizedUser.IsBanned)
        {
            context.Fail("This account is banned.");
            return;
        }

        context.Identity!.AddClaim(new Claim(
            AuthenticatedUser.UserIdClaim,
            synchronizedUser.Id.ToString()));
        context.Identity.AddClaim(new Claim(ClaimTypes.Name, synchronizedUser.DisplayName));
        context.Properties.IsPersistent = true;
        context.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30);
    }

    private static string RequiredString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!
            : throw new InvalidOperationException($"Google did not return {propertyName}.");

    private static string? FirstNonEmpty(IConfiguration configuration, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool IsLocalReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) &&
        returnUrl.StartsWith('/') &&
        !returnUrl.StartsWith("//") &&
        !returnUrl.StartsWith("/\\");
}
