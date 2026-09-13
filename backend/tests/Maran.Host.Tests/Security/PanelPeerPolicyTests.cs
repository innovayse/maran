using Maran.Host.Security;

namespace Maran.Host.Tests.Security;

/// <summary>
/// The rule deciding which unix uid may use the panel's listening socket.
/// </summary>
/// <remarks>
/// Small, and the smallest test here is the important one. The panel's whole address-trust story
/// now rests on "the peer is nginx", and the way that story fails silently is for an unconfigured
/// uid to read as "anybody" — the same shape as an empty <c>KnownProxies</c> list, which does not
/// mean "trust nobody" but "skip the check". Absence must deny.
/// </remarks>
public sealed class PanelPeerPolicyTests
{
    /// <summary>The one configured uid is permitted.</summary>
    [Fact]
    public void The_configured_uid_is_permitted()
    {
        var policy = new PanelPeerPolicy(33);

        Assert.True(policy.Permits(33));
    }

    /// <summary>Any uid other than the configured one is refused.</summary>
    [Fact]
    public void Any_other_uid_is_refused()
    {
        var policy = new PanelPeerPolicy(33);

        Assert.False(policy.Permits(1000));
    }

    /// <summary>Root is not special-cased into the allow-list.</summary>
    [Fact]
    public void Root_gets_no_exception()
    {
        // An allow-list of exactly one is the only rule that cannot be widened by accident, and a
        // root process that wants the panel can ask as the web server user.
        var policy = new PanelPeerPolicy(33);

        Assert.False(policy.Permits(0));
    }

    /// <summary>A policy with no configured uid permits nobody at all.</summary>
    [Fact]
    public void An_unconfigured_policy_permits_nobody()
    {
        // The fail-open mutation this test exists for: making Permits return true when no uid is
        // configured turns the socket into an open door the moment panel.env loses one line.
        var policy = new PanelPeerPolicy(null);

        Assert.False(policy.IsConfigured);
        Assert.False(policy.Permits(0));
        Assert.False(policy.Permits(33));
        Assert.False(policy.Permits(1000));
    }
}
