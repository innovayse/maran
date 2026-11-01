using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Domain.Enums;
using Maran.Modules.Monitoring.Persistence;
using Maran.Modules.Monitoring.Services;
using Maran.Modules.Monitoring.Tests.TestSupport;

namespace Maran.Modules.Monitoring.Tests.Services;

/// <summary>
/// The window Identity reads when it refuses a two-factor code (issue #150): it answers the latest
/// reading, not the debounced alert, and "never read" is not "unsynchronised".
/// </summary>
public sealed class ClockSyncStatusTests
{
    /// <summary>The instant the observations are made.</summary>
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>With no reading ever taken, the clock is not reported unsynchronised.</summary>
    [Fact]
    public async Task With_no_reading_the_clock_is_not_reported_unsynchronised()
    {
        await using var dbContext = MonitoringTestContext.Create();

        Assert.False(await new ClockSyncStatus(dbContext).IsClockUnsynchronizedAsync(CancellationToken.None));
    }

    /// <summary>One breaching reading is enough, long before the alert itself is raised.</summary>
    [Fact]
    public async Task One_breaching_reading_reports_unsynchronised_before_any_alert()
    {
        await using var dbContext = MonitoringTestContext.Create();
        await SeedAsync(dbContext, breaching: true);

        Assert.True(await new ClockSyncStatus(dbContext).IsClockUnsynchronizedAsync(CancellationToken.None));
    }

    /// <summary>A synchronised latest reading clears it: the inverse control.</summary>
    [Fact]
    public async Task A_synchronised_latest_reading_is_not_reported_unsynchronised()
    {
        await using var dbContext = MonitoringTestContext.Create();
        await SeedAsync(dbContext, breaching: true, thenHealthy: true);

        Assert.False(await new ClockSyncStatus(dbContext).IsClockUnsynchronizedAsync(CancellationToken.None));
    }

    /// <summary>Stores the clock's alert row after one or two observations.</summary>
    /// <param name="dbContext">The module's context.</param>
    /// <param name="breaching">Whether the first observation breached.</param>
    /// <param name="thenHealthy">Whether a healthy observation follows it.</param>
    /// <returns>A task that completes when the row is saved.</returns>
    private static async Task SeedAsync(
        MonitoringDbContext dbContext,
        bool breaching,
        bool thenHealthy = false)
    {
        var row = new AlertState(Guid.NewGuid(), AlertKind.ClockUnsynchronized, AlertEvaluator.SystemClockSubject, Start);
        row.Observe(breaching, Start);
        if (thenHealthy)
        {
            row.Observe(false, Start.AddMinutes(1));
        }

        dbContext.AlertStates.Add(row);
        await dbContext.SaveChangesAsync();
    }
}
