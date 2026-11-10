using Maran.Modules.Marketplace.Services;

namespace Maran.Modules.Marketplace.Tests.Services;

/// <summary>What an update check found withdrawn is replaced by the next check, not added to.</summary>
public sealed class ObservedRevocationsTests
{
    /// <summary>A new check replaces the previous result, so a module no longer withdrawn is no longer marked.</summary>
    [Fact]
    public void A_new_check_replaces_the_previous_result()
    {
        var revocations = new ObservedRevocations();

        revocations.Replace(["mail"]);
        Assert.True(revocations.IsRevoked("mail"));
        Assert.False(revocations.IsRevoked("dns"));

        revocations.Replace(["dns"]);
        Assert.False(revocations.IsRevoked("mail"));
        Assert.True(revocations.IsRevoked("dns"));
    }
}
