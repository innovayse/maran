using Maran.Modules.Notifications.Persistence;
using Maran.Modules.Notifications.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Notifications.Commands.SaveSmtpSettings;

/// <summary>
/// Handles <see cref="SaveSmtpSettingsCommand"/> by writing the singleton row and forgetting what the
/// sender had cached.
/// </summary>
/// <remarks>
/// <para>
/// <b>Insert-or-update against a fixed key, never "find the first row".</b> The primary key is the
/// constant <c>SmtpSettings.SingletonId</c>, so two concurrent saves contend on one row rather than
/// each creating one — and a panel can never end up with two answers to "where does the mail go",
/// whichever of which happened to be loaded first.
/// </para>
/// <para>
/// <b>The cache is invalidated AFTER the commit.</b> Doing it before would let a concurrent read
/// re-cache the old row — the new one is not visible until the transaction commits — and the panel
/// would then keep sending through the old server until it was restarted.
/// </para>
/// <para>
/// <b>The audit entry names the server, never the credential.</b> The journal is append-only and
/// never deleted; a password in it is a password kept for ever, in a place an operator reads
/// (rules/security.md item 8).
/// </para>
/// </remarks>
public sealed class SaveSmtpSettingsCommandHandler
{
    /// <summary>The module's database context, which owns the settings row.</summary>
    private readonly NotificationsDbContext _dbContext;

    /// <summary>The sender's cached copy of the settings, dropped once the new ones are committed.</summary>
    private readonly SmtpSettingsCache _cache;

    /// <summary>The panel's append-only journal.</summary>
    private readonly NotificationsAuditJournal _journal;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="cache">The sender's cached copy of the settings.</param>
    /// <param name="journal">The panel's append-only journal.</param>
    /// <param name="clock">The panel's clock, which stamps the row.</param>
    public SaveSmtpSettingsCommandHandler(
        NotificationsDbContext dbContext,
        SmtpSettingsCache cache,
        NotificationsAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _cache = cache;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Saves the panel's mail settings.</summary>
    /// <param name="command">The validated settings.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success. The settings are the caller's own input; there is nothing to hand back.</returns>
    public async Task<Result<bool>> HandleAsync(SaveSmtpSettingsCommand command, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var settings = await _dbContext.SmtpSettings
            .FirstOrDefaultAsync(row => row.Id == Domain.Entities.SmtpSettings.SingletonId, cancellationToken);

        if (settings is null)
        {
            // No stored password to keep on the very first save, so a null becomes the empty string:
            // a relay that takes no credentials is a legitimate configuration and must not be forced
            // to invent one.
            settings = new Domain.Entities.SmtpSettings(
                command.Host,
                command.Port,
                command.Security,
                command.Username,
                command.Password ?? string.Empty,
                command.FromAddress,
                command.FromName,
                command.AlertRecipient,
                now);

            _dbContext.SmtpSettings.Add(settings);
        }
        else
        {
            settings.Replace(
                command.Host,
                command.Port,
                command.Security,
                command.Username,
                command.Password,
                command.FromAddress,
                command.FromName,
                command.AlertRecipient,
                now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _cache.Invalidate();

        await _journal.RecordRequestAsync(
            AuditActions.SmtpSettingsSaved,
            command.Host,
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        return Result<bool>.Ok(true);
    }
}
