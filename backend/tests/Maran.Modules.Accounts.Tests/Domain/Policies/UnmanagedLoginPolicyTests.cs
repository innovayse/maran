using Maran.Agent.Client.Services.AccountsService;
using Maran.Modules.Accounts.Domain.Policies;

namespace Maran.Modules.Accounts.Tests.Domain.Policies;

/// <summary>
/// Behavioral contract of <see cref="UnmanagedLoginPolicy"/>: the three things a suspension
/// attestation can honestly say about the logins the panel does not own, the fact that they are three
/// and not two, and the two wordings the absent one is given.
/// </summary>
public sealed class UnmanagedLoginPolicyTests
{
    /// <summary>The count of logins the panel does not own is named on the attestation.</summary>
    [Fact]
    public void The_count_of_logins_the_panel_does_not_own_is_named_on_the_attestation()
    {
        // The threat note covering this work asks a reviewer to be sure the panel SHOWS this number
        // rather than swallowing it. The whole clause is asserted, because a check for "2" alone
        // would also pass over a sentence that reported two of something else.
        var described = UnmanagedLoginPolicy.Describe(State(2, LoginTransferProtocol.Ftps));

        Assert.Equal(
            ", and 2 login(s) sharing this account's uid that the panel did not create and does not lock",
            described);
    }

    /// <summary>A measured zero is stated as a completeness claim rather than left unsaid.</summary>
    [Fact]
    public void A_measured_zero_is_stated_as_a_completeness_claim_rather_than_left_unsaid()
    {
        // Zero is a fact the host measured, and it is the fact that makes the rest of the attestation
        // complete. Rendering nothing would leave silence to carry it, which is indistinguishable
        // from the host never having answered.
        var described = UnmanagedLoginPolicy.Describe(State(0, LoginTransferProtocol.Sftp));

        Assert.Equal(", and no login outside the panel's own shares this account's uid", described);
    }

    /// <summary>A count the host never gave is stated as unknown and never as zero.</summary>
    [Fact]
    public void A_count_the_host_never_gave_is_stated_as_unknown_and_never_as_zero()
    {
        // The vacuity guard for the pair above, on the axis that can go blind: the wire carries a
        // uint32, so an agent predating the field is indistinguishable from one reporting zero by the
        // NUMBER alone. A policy that read the number only would answer these two identically, and
        // the completeness claim above would then be printed over an answer nobody gave.
        var described = UnmanagedLoginPolicy.Describe(State(0, LoginTransferProtocol.Unspecified));

        Assert.Equal(
            ", and the host did not say how many logins share this account's uid without the panel "
                + "having created them, so that number is unknown rather than zero",
            described);
    }

    /// <summary>An account with no login at all is told what to look at instead of being told "unknown".</summary>
    [Fact]
    public void An_account_with_no_login_at_all_is_told_what_to_look_at_instead_of_being_told_unknown()
    {
        // The commonest account on any host, and the reading this clause was rewritten for: with no
        // login in the answer the inference has nothing to infer from, so "unknown" was formally right
        // and useless on every ordinary suspension. The whole clause is asserted, and it must be
        // NEITHER of the other two unmeasured-or-zero wordings — a substring check for "uid" passes
        // over all three.
        var described = UnmanagedLoginPolicy.Describe(new AccountSuspensionStateDto(
            true, AccountLoginPasswordState.Locked, true, [], 0, 0, 0, []));

        Assert.Equal(
            ", and no transfer login was reported, so nothing in the host's answer can say whether a "
                + "login outside the panel's own shares this account's uid: read the host's passwd for "
                + "entries carrying it",
            described);
    }

    /// <summary>No wording of an absent count ever claims a measurement or renders as zero.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void No_wording_of_an_absent_count_ever_claims_a_measurement_or_renders_as_zero(bool anyLogin)
    {
        // The standard this sentence is held to, asserted directly on both unmeasured shapes: an
        // absent count must never read as the measured zero's completeness claim. This is what stops
        // the two new wordings drifting into one that sounds like an answer.
        var described = anyLogin
            ? UnmanagedLoginPolicy.Describe(State(0, LoginTransferProtocol.Unspecified))
            : UnmanagedLoginPolicy.Describe(new AccountSuspensionStateDto(
                true, AccountLoginPasswordState.Locked, true, [], 0, 0, 0, []));

        Assert.DoesNotContain("no login outside the panel's own shares", described, StringComparison.Ordinal);
    }

    /// <summary>A count above zero is a measurement whatever else the host said.</summary>
    [Fact]
    public void A_count_above_zero_is_a_measurement_whatever_else_the_host_said()
    {
        // A non-zero count can only come from an agent that counted, so it needs no corroboration —
        // and an implementation that demanded a stated protocol before believing any count would
        // report a real warning as "unknown", which is the one reading that hides it.
        var described = UnmanagedLoginPolicy.Describe(State(3, LoginTransferProtocol.Unspecified));

        Assert.Equal(
            ", and 3 login(s) sharing this account's uid that the panel did not create and does not lock",
            described);
    }

    /// <summary>Builds a host answer carrying one login and the given unmanaged count.</summary>
    /// <param name="unmanaged">How many passwd entries share the uid without being the panel's.</param>
    /// <param name="protocol">The daemon the host stated for the one reported login.</param>
    /// <returns>The observation to word.</returns>
    private static AccountSuspensionStateDto State(uint unmanaged, LoginTransferProtocol protocol)
    {
        return new AccountSuspensionStateDto(
            true,
            AccountLoginPasswordState.Locked,
            true,
            [],
            0,
            0,
            0,
            [new FileTransferLoginSuspensionFactDto("acme_web", true, protocol)],
            unmanaged);
    }
}
