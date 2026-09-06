using GeometryDashPlace.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace GeometryDashPlace.Web.Auth;

public static class AdminPolicy
{
    public const string Name = "Admin";
}

public sealed class AdminRequirement : IAuthorizationRequirement;

public sealed class AdminAuthorizationHandler(
    IDbContextFactory<GeometryDashPlaceDbContext> contextFactory)
    : AuthorizationHandler<AdminRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdminRequirement requirement)
    {
        if (!AuthenticatedUser.TryGetUserId(context.User, out var userId))
        {
            return;
        }

        await using var database = await contextFactory.CreateDbContextAsync();
        if (await database.Users.AsNoTracking().AnyAsync(user =>
                user.Id == userId && user.IsAdmin && !user.IsBanned))
        {
            context.Succeed(requirement);
        }
    }
}

public sealed class SiteOwnership
{
    private readonly HashSet<string> _emails;

    public SiteOwnership(string? configuredEmails)
    {
        _emails = (configuredEmails ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool IsOwner(string email) => _emails.Contains(email.Trim());
}
