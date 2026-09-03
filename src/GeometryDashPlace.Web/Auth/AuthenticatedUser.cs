using System.Security.Claims;

namespace GeometryDashPlace.Web.Auth;

public static class AuthenticatedUser
{
    public const string UserIdClaim = "geometrydashplace:user_id";

    public static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(UserIdClaim), out userId);
}
