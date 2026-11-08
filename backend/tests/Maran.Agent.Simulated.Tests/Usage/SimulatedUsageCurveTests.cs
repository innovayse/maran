using Maran.Agent.Simulated.State;
using Maran.Agent.Simulated.Usage;

namespace Maran.Agent.Simulated.Tests.Usage;

/// <summary>The disk and load figures a simulated host reports are functions of a name and a moment only.</summary>
public sealed class SimulatedUsageCurveTests
{
    /// <summary>Bytes in a mebibyte.</summary>
    private const ulong Mebibyte = 1024UL * 1024;

    /// <summary>When the accounts under test are said to have been created.</summary>
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A new account starts between 8 and 200 MiB, the range the base promises.</summary>
    [Fact]
    public void A_new_accounts_usage_is_inside_the_documented_base_range()
    {
        for (var index = 0; index < 100; index++)
        {
            var used = SimulatedUsageCurve.AccountUsedBytes(Account("user" + index, 0, null), Created);

            Assert.InRange(used, 8 * Mebibyte, 200 * Mebibyte);
        }
    }

    /// <summary>Two accounts do not all show the same figure.</summary>
    [Fact]
    public void Different_accounts_use_different_amounts()
    {
        var figures = Enumerable.Range(0, 20)
            .Select(
                index => { return SimulatedUsageCurve.AccountUsedBytes(Account("user" + index, 0, null), Created); })
            .Distinct()
            .Count();

        Assert.True(figures > 15);
    }

    /// <summary>Usage never shrinks as time passes.</summary>
    [Fact]
    public void Usage_never_shrinks_over_time()
    {
        var account = Account("acme", 0, null);
        var previous = 0UL;

        for (var day = 0; day < 40; day++)
        {
            var used = SimulatedUsageCurve.AccountUsedBytes(account, Created.AddDays(day));

            Assert.True(used >= previous);
            previous = used;
        }
    }

    /// <summary>A moment before the account existed is treated as its creation, never as negative growth.</summary>
    [Fact]
    public void A_moment_before_creation_reports_the_base()
    {
        var account = Account("acme", 0, null);

        Assert.Equal(
            SimulatedUsageCurve.AccountUsedBytes(account, Created),
            SimulatedUsageCurve.AccountUsedBytes(account, Created.AddDays(-5)));
    }

    /// <summary>A pinned figure wins over both the curve and the quota.</summary>
    [Fact]
    public void A_pinned_figure_is_reported_as_it_is()
    {
        var account = Account("acme", Mebibyte, 9 * Mebibyte);

        Assert.Equal(9 * Mebibyte, SimulatedUsageCurve.AccountUsedBytes(account, Created.AddDays(100)));
    }

    /// <summary>The host's free disk never underflows when the accounts together outgrow the disk.</summary>
    [Fact]
    public void Host_disk_availability_never_underflows()
    {
        var facts = SimulatedHostFacts.Baseline;

        var metrics = SimulatedUsageCurve.HostMetrics(facts, ulong.MaxValue / 4, Created);

        Assert.Equal(facts.TotalDiskBytes, metrics.DiskUsedBytes);
        Assert.Equal(0UL, metrics.DiskAvailableBytes);
    }

    /// <summary>The free-space floor the host reports is the one in its facts.</summary>
    [Fact]
    public void The_free_space_floor_is_taken_from_the_host_facts()
    {
        var facts = SimulatedHostFacts.Baseline with { FreeSpaceFloorBytes = 123456789 };

        Assert.Equal(123456789UL, SimulatedUsageCurve.HostMetrics(facts, 0, Created).FreeSpaceFloorBytes);
    }

    /// <summary>Builds an account record for a curve test.</summary>
    /// <param name="username">The account's system user name.</param>
    /// <param name="quotaBytes">Its quota; zero is no limit.</param>
    /// <param name="pinned">A pinned usage, or null to follow the curve.</param>
    /// <returns>The record, created at <see cref="Created"/>.</returns>
    private static SimulatedAccount Account(string username, ulong quotaBytes, ulong? pinned)
    {
        return new SimulatedAccount(
            username,
            10000,
            "/home/" + username,
            quotaBytes,
            Created,
            false,
            SimulatedAccount.UnlimitedSlices,
            pinned);
    }
}
