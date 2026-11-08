using System.Globalization;
using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Persistence;
using Maran.Modules.Monitoring.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Monitoring.Commands.SaveAlertThresholds;

/// <summary>
/// Handles <see cref="SaveAlertThresholdsCommand"/>: stores the thresholds and journals the change
/// with the figures before and after (issue #142).
/// </summary>
/// <remarks>
/// <para>
/// <b>Journalled with old and new values.</b> Lowering a threshold is also how an alert is silenced,
/// so "who changed the disk alarm from 90 to 99, and when" is the question this entry answers. A save
/// that repeats the current figures is journalled too: an administrator asked, and the journal
/// records what was asked.
/// </para>
/// <para>
/// A panel nobody has configured has no row, so the first save starts from the defaults and adds it;
/// the "before" half of that first entry is therefore the default, which is what was in force.
/// </para>
/// </remarks>
public sealed class SaveAlertThresholdsCommandHandler
{
    /// <summary>How an unwatched threshold reads in the journal.</summary>
    private const string Off = "off";

    /// <summary>The module's database context, which owns the stored thresholds.</summary>
    private readonly MonitoringDbContext _dbContext;

    /// <summary>The module's audit journal.</summary>
    private readonly MonitoringAuditJournal _journal;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="journal">The module's audit journal.</param>
    /// <param name="clock">The panel's clock, which stamps the stored thresholds.</param>
    public SaveAlertThresholdsCommandHandler(MonitoringDbContext dbContext, MonitoringAuditJournal journal, IClock clock)
    {
        _dbContext = dbContext;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Stores and journals the thresholds.</summary>
    /// <param name="command">The validated thresholds.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success; the thresholds are the caller's own input and there is nothing to hand back.</returns>
    public async Task<Result> HandleAsync(SaveAlertThresholdsCommand command, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var thresholds = await _dbContext.AlertThresholds
            .FirstOrDefaultAsync(row => row.Id == AlertThresholds.SingletonId, cancellationToken);

        if (thresholds is null)
        {
            thresholds = AlertThresholds.CreateDefault(now);
            _dbContext.AlertThresholds.Add(thresholds);
        }

        var before = Describe(thresholds.DiskPercent, thresholds.CpuPercent, thresholds.MemoryPercent);

        thresholds.Replace(command.DiskPercent, command.CpuPercent, command.MemoryPercent, now);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var after = Describe(command.DiskPercent, command.CpuPercent, command.MemoryPercent);

        await _journal.RecordRequestAsync(
            MonitoringAuditActions.AlertThresholdsSaved,
            string.Format(CultureInfo.InvariantCulture, "{0} -> {1}", before, after),
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        return Result.Ok();
    }

    /// <summary>Spells the three thresholds for the journal, in a form that does not depend on a language.</summary>
    /// <param name="diskPercent">The disk threshold.</param>
    /// <param name="cpuPercent">The processor threshold, or <c>null</c> when not watched.</param>
    /// <param name="memoryPercent">The memory threshold, or <c>null</c> when not watched.</param>
    /// <returns>For example <c>disk=90;cpu=off;memory=80</c>.</returns>
    private static string Describe(int diskPercent, int? cpuPercent, int? memoryPercent)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "disk={0};cpu={1};memory={2}",
            diskPercent,
            cpuPercent?.ToString(CultureInfo.InvariantCulture) ?? Off,
            memoryPercent?.ToString(CultureInfo.InvariantCulture) ?? Off);
    }
}
