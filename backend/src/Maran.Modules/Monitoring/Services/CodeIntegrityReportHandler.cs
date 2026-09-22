using Maran.Modules.Monitoring.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Monitoring.Services;

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
            await _evaluator.EvaluateAsync(
                diskUsedPercent: null,
                services: [],
                sftpJailStatus: null,
                quotaStatus: null,
                codeIntegrityReport: report,
                report.ObservedAt,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Per ICodeIntegrityReportSink's own contract: the caller is a closed, foreign component
            // that cannot itself react to an exception meaningfully. Logged here, on this side of the
            // boundary, rather than allowed to propagate into PluginLoader's AssemblyLoadContext.
            LogReportFailed(_logger, report.InstalledVersion, report.Outcome, ex);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to record a code-integrity report for version {InstalledVersion} (outcome {Outcome}).")]
    private static partial void LogReportFailed(ILogger logger, string installedVersion, CodeIntegrityOutcome outcome, Exception exception);
}
