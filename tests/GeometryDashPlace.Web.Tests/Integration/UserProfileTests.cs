using GeometryDashPlace.Web.Data;
using GeometryDashPlace.Web.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GeometryDashPlace.Web.Tests.Integration;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class UserProfileTests(PostgreSqlIntegrationFixture database)
{
    [PostgreSqlFact]
    public async Task CompletingProfile_PersistsUsernameAndAvatar()
    {
        var scenario = await database.CreateScenarioAsync(isProfileCompleted: false);
        using var scope = database.Application.Services.CreateScope();
        var profiles = scope.ServiceProvider.GetRequiredService<IUserProfileService>();

        var result = await profiles.SaveAsync(
            scenario.UserId,
            "Dash_Player",
            ProfileAvatarChoice.Upload,
            new ProfileAvatarUpload(CreatePng()));

        Assert.True(result.Succeeded, result.Error);
        await using var context = database.CreateDbContext();
        var user = await context.Users.AsNoTracking().SingleAsync(
            candidate => candidate.Id == scenario.UserId);
        Assert.True(user.IsProfileCompleted);
        Assert.Equal("Dash_Player", user.Username);
        Assert.Equal("DASH_PLAYER", user.NormalizedUsername);
        Assert.StartsWith($"/avatars/{scenario.UserId:N}.png?v=", user.AvatarUrl);
        Assert.NotNull(user.AvatarPng);
    }

    [PostgreSqlFact]
    public async Task Username_IsUniqueRegardlessOfCase()
    {
        var scenario = await database.CreateScenarioAsync(
            userCount: 2,
            isProfileCompleted: false);
        using var scope = database.Application.Services.CreateScope();
        var profiles = scope.ServiceProvider.GetRequiredService<IUserProfileService>();

        var first = await profiles.SaveAsync(
            scenario.UserIds[0],
            "Same_Player",
            ProfileAvatarChoice.Default,
            null);
        var second = await profiles.SaveAsync(
            scenario.UserIds[1],
            "same_player",
            ProfileAvatarChoice.Default,
            null);

        Assert.True(first.Succeeded, first.Error);
        Assert.False(second.Succeeded);
        Assert.Equal("This username is already taken.", second.Error);
    }

    private static byte[] CreatePng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
}
