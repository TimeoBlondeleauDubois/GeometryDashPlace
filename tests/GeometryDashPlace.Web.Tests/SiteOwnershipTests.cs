using GeometryDashPlace.Web.Auth;

namespace GeometryDashPlace.Web.Tests;

public sealed class SiteOwnershipTests
{
    [Fact]
    public void ConfiguredOwnerEmails_AreCaseInsensitiveAndTrimmed()
    {
        var ownership = new SiteOwnership(" owner@example.test,SECOND@example.test ");

        Assert.True(ownership.IsOwner("OWNER@example.test"));
        Assert.True(ownership.IsOwner(" second@example.test "));
        Assert.False(ownership.IsOwner("user@example.test"));
    }
}
