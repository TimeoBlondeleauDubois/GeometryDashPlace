using GeometryDashPlace.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace GeometryDashPlace.Web.Profiles;

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/avatars/{userId:guid}.png", GetAvatarAsync)
            .AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> GetAvatarAsync(
        Guid userId,
        HttpContext httpContext,
        IDbContextFactory<GeometryDashPlaceDbContext> contextFactory,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var avatar = await context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId && user.AvatarPng != null)
            .Select(user => user.AvatarPng)
            .SingleOrDefaultAsync(cancellationToken);
        if (avatar is null)
        {
            return Results.NotFound();
        }

        httpContext.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        httpContext.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(avatar, "image/png");
    }
}
