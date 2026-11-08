using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Persistence;
using Maran.Modules.Monitoring.Services;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Monitoring.Tests.Services;

#pragma warning disable CS0618 // The sink still receives the superseded flat report; building one is the point of these cases.

/// <summary>
/// The code-integrity sink's idempotency per <c>(InstalledVersion, ObservedAt)</c>: a report the closed
/// PluginLoader delivers again is the same event, not another consecutive breach.
/// </summary>
public sealed class CodeIntegrityReportHandlerTests
{
    /// <summary>The instant the first report of every fixture was observed.</summary>
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>One drifted report delivered more often than the debounce needs raises nothing.</summary>
    [Fact]
    public async Task A_drifted_report_redelivered_many_times_counts_once_and_raises_nothing()
    {
        await using var dbContext = MonitoringTestContext.Create();
        using var scopes = NewScopes(dbContext);
        var handler = NewHandler(scopes, dbContext);
        var report = Drifted(Start);

        for (var delivery = 0; delivery < AlertState.BreachesBeforeAlert * 2; delivery++)
        {
            await handler.ReportAsync(report, CancellationToken.None);
        }

        Assert.Empty(scopes.Bus.Mail);
        var row = await dbContext.AlertStates.SingleAsync(state => state.Subject == AlertEvaluator.InstalledFilesSubject);
        Assert.Equal(1, row.ConsecutiveBreaches);
    }

    /// <summary>The inverse control: the same number of reports, each a new observation, does raise.</summary>
    [Fact]
    public async Task The_same_number_of_distinct_drifted_reports_raises_one_alert()
    {
        await using var dbContext = MonitoringTestContext.Create();
        using var scopes = NewScopes(dbContext);
        var handler = NewHandler(scopes, dbContext);

        for (var delivery = 0; delivery < AlertState.BreachesBeforeAlert; delivery++)
        {
            await handler.ReportAsync(Drifted(Start.AddMinutes(delivery)), CancellationToken.None);
        }

        Assert.Single(scopes.Bus.Mail);
    }

    /// <summary>A repeat after a later report is still dropped: it must not undo what the later one recorded.</summary>
    [Fact]
    public async Task A_repeat_of_an_earlier_report_after_a_later_one_changes_nothing()
    {
        await using var dbContext = MonitoringTestContext.Create();
        using var scopes = NewScopes(dbContext);
        var handler = NewHandler(scopes, dbContext);

        await handler.ReportAsync(Drifted(Start), CancellationToken.None);
        await handler.ReportAsync(Drifted(Start.AddMinutes(1)), CancellationToken.None);
        await handler.ReportAsync(Drifted(Start), CancellationToken.None);

        var row = await dbContext.AlertStates.SingleAsync(state => state.Subject == AlertEvaluator.InstalledFilesSubject);
        Assert.Equal(2, row.ConsecutiveBreaches);
        Assert.Equal(Start.AddMinutes(1), row.LastObservedAt);
    }

    /// <summary>Builds the container the evaluator is resolved from.</summary>
    /// <param name="dbContext">The module's context.</param>
    /// <returns>The factory.</returns>
    private static TestScopeFactory NewScopes(MonitoringDbContext dbContext)
    {
        return new TestScopeFactory(
            dbContext,
            new StubAgentMonitorClient(),
            new StubAlertRecipientDirectory("ops@example.com"),
            new RecordingAuditWriter());
    }

    /// <summary>Builds the handler under test over the shared context.</summary>
    /// <param name="scopes">The container serving the evaluator.</param>
    /// <param name="dbContext">The module's context.</param>
    /// <returns>The handler.</returns>
    private static CodeIntegrityReportHandler NewHandler(TestScopeFactory scopes, MonitoringDbContext dbContext)
    {
        return new CodeIntegrityReportHandler(
            scopes.Resolve<AlertEvaluator>(),
            dbContext,
            NullLogger<CodeIntegrityReportHandler>.Instance);
    }

    /// <summary>Builds a drifted report for one version, observed at the given instant.</summary>
    /// <param name="observedAt">When the closed side made the observation.</param>
    /// <returns>The report.</returns>
    private static CodeIntegrityReport Drifted(DateTimeOffset observedAt)
    {
        return new CodeIntegrityReport(
            CodeIntegrityOutcome.Drifted,
            "1.4.2",
            ["api/Maran.Host"],
            null,
            observedAt);
    }
}

#pragma warning restore CS0618
