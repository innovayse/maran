using Maran.Sdk.Contracts;

namespace Maran.Sdk.Tests.Contracts;

/// <summary>The credential material a site snapshot carries across modules stays out of anything that prints a user.</summary>
public sealed class SiteBasicAuthUserSnapshotTests
{
    /// <summary>A user prints its name and not its hash.</summary>
    [Fact]
    public void A_user_prints_its_name_and_not_its_hash()
    {
        var text = new SiteBasicAuthUserSnapshot("alice", "$2y$12$verysecrethash").ToString();

        Assert.Contains("alice", text, StringComparison.Ordinal);
        Assert.DoesNotContain("verysecrethash", text, StringComparison.Ordinal);
    }
}
