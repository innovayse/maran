using Maran.Modules.Accounts.Domain.Policies;
using Maran.Sdk.Events;

namespace Maran.Modules.Accounts.Tests.Domain.Policies;

/// <summary>
/// Behavioral contract of <see cref="SessionCullPolicy"/>: the four readings a suspension attestation
/// can carry about the sessions it ended, and the two pairs of them that must never collapse into
/// each other — a measured zero into an absent count, and an absent count into a cascade nobody
/// answered.
/// </summary>
public sealed class SessionCullPolicyTests
{
    /// <summary>A count the host gave is stated as a number, with what it cost the customer.</summary>
    [Fact]
    public void A_count_the_host_gave_is_stated_as_a_number()
    {
        // The whole clause and not a substring: a probe for "3" alone would pass over a sentence
        // reporting three of something else, which is the shape of the mistake that had this line
        // saying "sftp logins" about FTPS credentials.
        var described = SessionCullPolicy.Describe(Reported(3));

        Assert.Equal(
            "a suspension also ends the account's open transfer sessions, and the host ended 3 of "
                + "them, cutting whatever they were transferring and leaving any partial file in the "
                + "account's home; ",
            described);
    }

    /// <summary>A measured none is stated as a completeness claim rather than as a silence.</summary>
    [Fact]
    public void A_measured_none_is_stated_as_a_completeness_claim()
    {
        // `pkill` exits 0 only when it matched at least one process, so a zero from the agent is
        // uniquely "nothing matched" — the state a suspension is trying to reach, and a fact the
        // attestation is entitled to state.
        var described = SessionCullPolicy.Describe(Reported(0));

        Assert.Equal(
            "a suspension also ends the account's open transfer sessions, and the host found none of "
                + "this account's to end; ",
            described);
    }

    /// <summary>A count the host never gave reads as unknown and never as none.</summary>
    [Fact]
    public void A_count_the_host_never_gave_reads_as_unknown_and_never_as_none()
    {
        var described = SessionCullPolicy.Describe(Reported(null));

        Assert.Equal(
            "a suspension also ends the account's open transfer sessions, and the host's answer "
                + "carried no count of them, so this line cannot say how many were ended, or that any "
                + "were; ",
            described);
    }

    /// <summary>A cascade no subscriber answered says only that the panel asked.</summary>
    [Fact]
    public void A_cascade_no_subscriber_answered_says_only_that_the_panel_asked()
    {
        // The fire-and-forget gap, stated as its own reading. A subscriber that does not exist cannot
        // throw, so a completed cascade is not evidence that anything ended — and wording this as
        // "the host did not say" would claim the host was asked something it never was.
        var described = SessionCullPolicy.Describe(new AccountSuspensionCascadeReport());

        Assert.Equal(
            "the panel asked the host to end the account's open transfer sessions, and no module "
                + "reported having done so, so this line cannot say that any were ended; ",
            described);
    }

    /// <summary>
    /// The vacuity guard, on the axis that can actually go blind: a measured none and an absent count
    /// differ ONLY in the optional field's presence, so a check that passed in both states would
    /// measure neither.
    /// </summary>
    [Fact]
    public void A_measured_none_and_an_absent_count_are_worded_differently()
    {
        var measured = SessionCullPolicy.Describe(Reported(0));
        var absent = SessionCullPolicy.Describe(Reported(null));
        var unanswered = SessionCullPolicy.Describe(new AccountSuspensionCascadeReport());

        Assert.NotEqual(measured, absent);
        Assert.NotEqual(absent, unanswered);

        // And the direction of the difference, not merely that there is one: only the measured
        // reading may make a claim about what is on the host, and neither absent reading may contain
        // the word the operator would read as a measurement.
        Assert.Contains("found none", measured, StringComparison.Ordinal);
        Assert.DoesNotContain("none of", absent, StringComparison.Ordinal);
        Assert.DoesNotContain("none of", unanswered, StringComparison.Ordinal);
    }

    /// <summary>
    /// The inverse control: a policy mutated to refuse to state anything would satisfy every
    /// assertion above that only ever hands it an absence, so a real count must render as a number
    /// and an absence must never render as one.
    /// </summary>
    /// <param name="ended">The count under test.</param>
    [Theory]
    [InlineData(1u)]
    [InlineData(7u)]
    [InlineData(4294967295u)]
    public void A_real_count_renders_as_that_number_and_an_absence_renders_as_no_number(uint ended)
    {
        Assert.Contains($"ended {ended} of them", SessionCullPolicy.Describe(Reported(ended)), StringComparison.Ordinal);
        Assert.DoesNotContain($"{ended}", SessionCullPolicy.Describe(Reported(null)), StringComparison.Ordinal);
    }

    /// <summary>A report a subscriber answered with <paramref name="ended"/>.</summary>
    /// <param name="ended">The count the subscriber carried, or <c>null</c> for an absent one.</param>
    /// <returns>The report to describe.</returns>
    private static AccountSuspensionCascadeReport Reported(uint? ended)
    {
        var report = new AccountSuspensionCascadeReport();
        report.ReportSessionCull(ended);

        return report;
    }
}
