using GeometryDashPlace.Web.Profiles;

namespace GeometryDashPlace.Web.Tests;

public sealed class UserProfileRulesTests
{
    [Theory]
    [InlineData("Player_1")]
    [InlineData("dash-player")]
    [InlineData("ABC")]
    public void ValidUsername_IsAccepted(string username)
    {
        Assert.Null(UserProfileRules.ValidateUsername(username));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("name with spaces")]
    [InlineData("player!")]
    [InlineData("abcdefghijklmnopqrstu")]
    public void InvalidUsername_IsRejected(string username)
    {
        Assert.NotNull(UserProfileRules.ValidateUsername(username));
    }

    [Fact]
    public void Normalization_IsCaseInsensitiveAndTrimsWhitespace()
    {
        Assert.Equal("PLAYER_1", UserProfileRules.NormalizeUsername("  Player_1 "));
    }

    [Fact]
    public void UploadedAvatar_RequiresARealPngHeaderAndSafeDimensions()
    {
        var invalid = new byte[] { 1, 2, 3 };
        var valid = CreatePng();

        Assert.NotNull(UserProfileRules.ValidatePng(invalid));
        Assert.Null(UserProfileRules.ValidatePng(valid));
    }

    private static byte[] CreatePng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
}
