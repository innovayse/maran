using Maran.Sdk.Events;

namespace Maran.Sdk.Tests.Events;

/// <summary>Behavioral contract of <see cref="AccountSuspensionCascadeReport"/>: three readings of the session cull, never collapsed.</summary>
public sealed class AccountSuspensionCascadeReportTests
{
    /// <summary>A report no subscriber wrote to says that nothing was reported.</summary>
    [Fact]
    public void A_report_nobody_wrote_to_says_nothing_was_reported()
    {
        var report = new AccountSuspensionCascadeReport();

        Assert.Equal(new SessionCullNotReported(), report.SessionCull);
    }

    /// <summary>A cull whose answer carried no count is reported without one, and is not a zero.</summary>
    [Fact]
    public void A_cull_whose_answer_carried_no_count_is_reported_without_one()
    {
        var report = new AccountSuspensionCascadeReport();

        report.ReportSessionCull(null);

        Assert.Equal(new SessionCullReportedWithoutCount(), report.SessionCull);
    }

    /// <summary>A measured zero stays a count and is not read as an absence.</summary>
    [Fact]
    public void A_measured_zero_stays_a_count()
    {
        var report = new AccountSuspensionCascadeReport();

        report.ReportSessionCull(0);

        Assert.Equal(new SessionCullEnded(0), report.SessionCull);
    }

    /// <summary>A count the host gave is carried as that number.</summary>
    [Fact]
    public void A_count_the_host_gave_is_carried_as_that_number()
    {
        var report = new AccountSuspensionCascadeReport();

        report.ReportSessionCull(7);

        Assert.Equal(new SessionCullEnded(7), report.SessionCull);
    }

    /// <summary>The last report wins, and a later absence replaces an earlier count rather than keeping it.</summary>
    [Fact]
    public void The_last_report_wins_and_a_later_absence_replaces_an_earlier_count()
    {
        var report = new AccountSuspensionCascadeReport();

        report.ReportSessionCull(7);
        report.ReportSessionCull(null);

        Assert.Equal(new SessionCullReportedWithoutCount(), report.SessionCull);
    }
}
