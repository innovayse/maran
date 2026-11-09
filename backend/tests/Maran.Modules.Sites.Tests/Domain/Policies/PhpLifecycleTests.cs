using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.Policies;

namespace Maran.Modules.Sites.Tests.Domain.Policies;

/// <summary>
/// Behavioral contract of <see cref="PhpLifecycle"/>: a version and a day become a status, the date
/// security support ends and the days left until it.
/// </summary>
/// <remarks>
/// The dates themselves are marked TO BE VERIFIED in the policy (no network when they were written), so
/// these tests pin the RULE at the table's current dates and the table's SHAPE (every supported version
/// has a row, the phases never run backwards), not the correctness of PHP's calendar. A corrected date
/// changes the fixed days below, which is the point of fixing them.
/// </remarks>
public sealed class PhpLifecycleTests
{
    /// <summary>The versions the agent can install and the panel must therefore be able to read.</summary>
    private static readonly string[] AgentSupportedVersions = ["7.4", "8.0", "8.1", "8.2", "8.3", "8.4"];

    /// <summary>A day before a version's active support ends reads supported, with days to the security end.</summary>
    [Fact]
    public void A_day_before_active_support_ends_reads_supported_with_the_days_to_security_end()
    {
        var reading = PhpLifecycle.Read("8.4", new DateOnly(2026, 11, 7));

        Assert.Equal(PhpSupportStatus.Supported, reading.Status);
        Assert.Equal(new DateOnly(2028, 12, 31), reading.EndOfLife);
        Assert.Equal(new DateOnly(2028, 12, 31).DayNumber - new DateOnly(2026, 11, 7).DayNumber, reading.DaysLeft);
    }

    /// <summary>The day active support ends is still a supported day; the next day is security-only.</summary>
    [Fact]
    public void The_last_day_of_active_support_is_supported_and_the_next_day_is_security_only()
    {
        var lastDay = PhpLifecycle.Read("8.3", new DateOnly(2025, 12, 31));
        var nextDay = PhpLifecycle.Read("8.3", new DateOnly(2026, 1, 1));

        Assert.Equal(PhpSupportStatus.Supported, lastDay.Status);
        Assert.Equal(730, lastDay.DaysLeft);
        Assert.Equal(PhpSupportStatus.SecurityOnly, nextDay.Status);
        Assert.Equal(729, nextDay.DaysLeft);
    }

    /// <summary>The day security support ends has zero days left; the next day is end of life.</summary>
    [Fact]
    public void The_last_day_of_security_support_has_zero_days_left_and_the_next_day_is_end_of_life()
    {
        var lastDay = PhpLifecycle.Read("8.2", new DateOnly(2026, 12, 31));
        var nextDay = PhpLifecycle.Read("8.2", new DateOnly(2027, 1, 1));

        Assert.Equal(PhpSupportStatus.SecurityOnly, lastDay.Status);
        Assert.Equal(0, lastDay.DaysLeft);
        Assert.Equal(PhpSupportStatus.EndOfLife, nextDay.Status);
    }

    /// <summary>An end-of-life version keeps the date it ended and has no days left.</summary>
    [Fact]
    public void An_end_of_life_version_keeps_the_date_it_ended_and_has_no_days_left()
    {
        var reading = PhpLifecycle.Read("7.4", new DateOnly(2026, 11, 7));

        Assert.Equal(PhpSupportStatus.EndOfLife, reading.Status);
        Assert.Equal(new DateOnly(2022, 11, 28), reading.EndOfLife);
        Assert.Null(reading.DaysLeft);
    }

    /// <summary>A version the table has no row for is unknown, never supported, and carries no date.</summary>
    /// <param name="version">A version with no row: newer than the table, or not a version at all.</param>
    [Theory]
    [InlineData("9.9")]
    [InlineData("5.6")]
    [InlineData("")]
    [InlineData("8.4.1")]
    public void A_version_without_a_row_is_unknown_and_never_supported(string version)
    {
        var reading = PhpLifecycle.Read(version, new DateOnly(2026, 11, 7));

        Assert.Equal(PhpSupportStatus.Unknown, reading.Status);
        Assert.Null(reading.EndOfLife);
        Assert.Null(reading.DaysLeft);
    }

    /// <summary>
    /// Census: every version the agent can install has a row, so none reads unknown on a day in the
    /// table's life.
    /// </summary>
    /// <remarks>
    /// The list of versions is the agent's supported range, written out here because the module may not
    /// reference the agent's crates. A new version added to the agent without a row in the table fails
    /// here by name, instead of reading as unknown on the version page.
    /// </remarks>
    [Fact]
    public void Every_version_the_agent_supports_has_a_lifecycle_row()
    {
        Assert.NotEmpty(AgentSupportedVersions);

        var withoutRow = AgentSupportedVersions
            .Where(version =>
            {
                return PhpLifecycle.Read(version, new DateOnly(2026, 11, 7)).Status == PhpSupportStatus.Unknown;
            })
            .ToList();

        Assert.True(withoutRow.Count == 0, "No lifecycle row for: " + string.Join(", ", withoutRow));
    }

    /// <summary>For every version the phases only ever move forward, and the days left agree with the date.</summary>
    /// <remarks>
    /// Walks every seventh day from 2020 to 2031 and checks the ordering supported, security-only,
    /// end-of-life never reverses and that the reported days left is exactly the distance to the
    /// reported date. A mistyped date that puts a security end before an active end would make a
    /// version go back from end of life to supported, and fails here. The vacuity guard is the count
    /// of readings, which proves the walk looked at something.
    /// </remarks>
    [Fact]
    public void The_phases_of_every_version_only_move_forward_and_days_left_agree_with_the_date()
    {
        var readings = 0;
        foreach (var version in AgentSupportedVersions)
        {
            var previous = PhpSupportStatus.Unknown;
            for (var day = new DateOnly(2020, 1, 1); day <= new DateOnly(2031, 12, 31); day = day.AddDays(7))
            {
                var reading = PhpLifecycle.Read(version, day);

                Assert.True(reading.Status >= previous || previous == PhpSupportStatus.Unknown, $"{version} went backwards on {day}.");
                Assert.NotEqual(PhpSupportStatus.Unknown, reading.Status);
                if (reading.DaysLeft is { } daysLeft)
                {
                    Assert.Equal(reading.EndOfLife!.Value.DayNumber - day.DayNumber, daysLeft);
                }

                previous = reading.Status;
                readings++;
            }

            Assert.Equal(PhpSupportStatus.EndOfLife, previous);
        }

        Assert.True(readings > 3000, $"The walk read only {readings} days.");
    }
}
