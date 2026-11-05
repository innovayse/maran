using Maran.Host.OpenApi;

namespace Maran.Host.Tests.OpenApi;

/// <summary>The version the document states, and the revision suffix it must never carry.</summary>
public sealed class OpenApiBuildVersionTests
{
    /// <summary>The source revision after the plus sign is removed and everything before it is kept.</summary>
    [Theory]
    [InlineData("1.0.0-beta.4+144f46c4", "1.0.0-beta.4")]
    [InlineData("1.2.3+abcdef0123456789", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    public void The_revision_after_the_plus_sign_is_removed(string informational, string expected)
    {
        Assert.Equal(expected, OpenApiBuildVersion.WithoutRevision(informational));
    }

    /// <summary>A missing or blank version, or a bare revision, is stated as the unversioned placeholder.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+144f46c4")]
    public void A_missing_version_is_stated_as_unversioned(string? informational)
    {
        Assert.Equal(OpenApiBuildVersion.Unversioned, OpenApiBuildVersion.WithoutRevision(informational));
    }
}
