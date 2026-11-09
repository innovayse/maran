using Maran.Modules.Monitoring.Domain.Enums;
using Maran.Modules.Monitoring.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Monitoring.Services;

#pragma warning disable CS0618 // This handler receives the closed PluginLoader's superseded report and converts it to a verdict at once; nothing past this file reads the report.

/// <summary>
/// The Monitoring module's implementation of <see cref="ICodeIntegrityReportSink"/> — the point
/// where a finding produced by the closed PluginLoader (outside this monorepo) reaches the panel's
/// own alert machine.
/// </summary>
/// <remarks>
/// <para>
/// <b>It holds no state of its own and re-implements no debounce.</b> <see cref="AlertEvaluator"/>
/// and <c>AlertState</c> already own consecutive-breach counting and raise/resolve deduplication;
/// this class's only job is to route the incoming report into
/// <see cref="AlertEvaluator.EvaluateAsync"/> with every other input left at its "unanswered"
/// value, so a code-integrity report never advances or resets an alert row it has nothing to say
/// about (disk usage, a stopped service, the SFTP jail, quota enforceability).
/// </para>
/// <para>
/// <b>A repeat of a report already recorded is dropped before it is evaluated.</b> The contract
/// requires idempotency per <c>(InstalledVersion, ObservedAt)</c> and forbids leaning on the debounce
/// alone: a redelivered drifted report would otherwise count as another consecutive breach and ten
/// redeliveries would raise an alert for one observation. The code-integrity rows are written with
/// the report's own <c>ObservedAt</c> and by no other source, so a row whose
/// <c>LastObservedAt</c> is not earlier than the report's means that observation, or a later one,
/// was already recorded. The installed version is not part of the comparison: the rows do not store
/// it, and two different versions observed at one instant of the closed side's clock is not a case
/// that exists.
/// </para>
/// <para>
/// <b>It honours <see cref="ICodeIntegrityReportSink"/>'s calling convention.</b> The caller is a
/// closed, foreign component on a schedule this repository does not control
/// (backend/src/Maran.Sdk/Interfaces/ICodeIntegrityReportSink.cs): this method never lets an
/// exception escape to it, and returns as soon as the round is committed.
/// </para>
/// </remarks>
public sealed partial class CodeIntegrityReportHandler : ICodeIntegrityReportSink
{
    /// <summary>The evaluator this handler routes every report into.</summary>
    private readonly AlertEvaluator _evaluator;

    /// <summary>The module's database context, saved even when the evaluator itself did not need to be asked anything else.</summary>
    private readonly MonitoringDbContext _dbContext;

    /// <summary>Logs a report this handler could not process, since its caller must never see the exception.</summary>
    private readonly ILogger<CodeIntegrityReportHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="evaluator">The evaluator this handler routes every report into.</param>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="logger">Logs a report this handler could not process.</param>
    public CodeIntegrityReportHandler(
        AlertEvaluator evaluator,
        MonitoringDbContext dbContext,
        ILogger<CodeIntegrityReportHandler> logger)
    {
        _evaluator = evaluator;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ReportAsync(CodeIntegrityReport report, CancellationToken cancellationToken)
    {
        try
        {
            var verdict = report.ToVerdict();

            if (await AlreadyRecordedAsync(verdict.ObservedAt, cancellationToken))
            {
                LogReportRepeated(_logger, report.InstalledVersion, verdict.ObservedAt);
                return;
            }

            await _evaluator.EvaluateAsync(
                diskUsedPercent: null,
                freeSpaceFloor: null,
                services: [],
                sftpJailStatus: null,
                quotaStatus: null,
                clockSync: null,
                codeIntegrityVerdict: verdict,
                hostLoad: null,
                verdict.ObservedAt,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Per ICodeIntegrityReportSink's own contract: the caller is a closed, foreign component
            // that cannot itself react to an exception meaningfully. Logged here, on this side of the
            // boundary, rather than allowed to propagate into PluginLoader's AssemblyLoadContext.
            LogReportFailed(_logger, report.InstalledVersion, report.Outcome.ToString(), ex);
        }
    }

    /// <summary>Whether a report observed at this instant, or later, has already been recorded.</summary>
    /// <param name="observedAt">When the closed PluginLoader made the observation.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><c>true</c> when the report is a repeat and must not be evaluated again.</returns>
    private async Task<bool> AlreadyRecordedAsync(DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        // Truncated to the microsecond the database stores, so a stored copy of this very instant
        // compares as equal and a sub-microsecond tick cannot make a repeat look new.
        var probe = new DateTimeOffset(observedAt.Ticks - (observedAt.Ticks % 10), observedAt.Offset);

        return await _dbContext.AlertStates.AnyAsync(
            row => row.Kind == AlertKind.CodeIntegrityDrifted && row.LastObservedAt >= probe,
            cancellationToken);
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Ignored a repeated code-integrity report for version {InstalledVersion} observed at {ObservedAt}.")]
    private static partial void LogReportRepeated(ILogger logger, string installedVersion, DateTimeOffset observedAt);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to record a code-integrity report for version {InstalledVersion} (outcome {Outcome}).")]
    private static partial void LogReportFailed(ILogger logger, string installedVersion, string outcome, Exception exception);
}

#pragma warning restore CS0618
