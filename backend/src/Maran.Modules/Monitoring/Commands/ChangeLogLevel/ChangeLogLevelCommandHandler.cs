using System.Globalization;
using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Persistence;
using Maran.Modules.Monitoring.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Monitoring.Commands.ChangeLogLevel;

/// <summary>
/// Handles <see cref="ChangeLogLevelCommand"/>: stores the choice, applies it to the running logger,
/// and journals it (issue #131).
/// </summary>
/// <remarks>
/// <para>
/// <b>Stored first, applied after the commit.</b> Applying first and then failing to store would leave
/// a panel logging at a level that the next restart silently undoes, with an administrator who saw it
/// work. In this order a failed write changes nothing at all, and the request fails with it.
/// </para>
/// <para>
/// <b>Journalled with the level before and after.</b> "Why did the logs go quiet on Tuesday" is the
/// question this entry answers, so the subject carries both ends of the change rather than only the
/// new level. A choice that repeats the current level is journalled too: an administrator asked, and
/// the journal records what was asked.
/// </para>
/// </remarks>
public sealed class ChangeLogLevelCommandHandler
{
    /// <summary>The module's database context, which owns the stored choice.</summary>
    private readonly MonitoringDbContext _dbContext;

    /// <summary>The running logger's level.</summary>
    private readonly IPanelLogLevelSwitch _levelSwitch;

    /// <summary>The module's audit journal.</summary>
    private readonly MonitoringAuditJournal _journal;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="levelSwitch">The running logger's level.</param>
    /// <param name="journal">The module's audit journal.</param>
    /// <param name="clock">The panel's clock, which stamps the stored choice.</param>
    public ChangeLogLevelCommandHandler(
        MonitoringDbContext dbContext,
        IPanelLogLevelSwitch levelSwitch,
        MonitoringAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _levelSwitch = levelSwitch;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Stores, applies and journals the chosen level.</summary>
    /// <param name="command">The validated choice.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success; the level is the caller's own input and there is nothing else to hand back.</returns>
    public async Task<Result<bool>> HandleAsync(ChangeLogLevelCommand command, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var previous = _levelSwitch.Current;

        var setting = await _dbContext.LogLevelSettings
            .FirstOrDefaultAsync(row => row.Id == LogLevelSetting.SingletonId, cancellationToken);

        if (setting is null)
        {
            _dbContext.LogLevelSettings.Add(new LogLevelSetting(command.Level, now));
        }
        else
        {
            setting.Change(command.Level, now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _levelSwitch.Apply(command.Level);

        await _journal.RecordRequestAsync(
            AuditActions.PanelLogLevelChanged,
            string.Format(CultureInfo.InvariantCulture, "{0} -> {1}", previous, command.Level),
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        return Result<bool>.Ok(true);
    }
}
