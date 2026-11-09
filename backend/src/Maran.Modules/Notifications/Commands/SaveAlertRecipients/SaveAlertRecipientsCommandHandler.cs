using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Persistence;
using Maran.Modules.Notifications.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Notifications.Commands.SaveAlertRecipients;

/// <summary>
/// Handles <see cref="SaveAlertRecipientsCommand"/> by making the stored list equal the requested one
/// and mirroring the first address into the mail settings for the previous release.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rows whose address stays are kept and moved, never deleted and re-inserted.</b> The address
/// column is unique, and one save that deleted and inserted the same address can have EF issue the
/// insert first and fail on its own index.
/// </para>
/// <para>
/// <b>The mirror is a rollback courtesy, not a second source.</b> The previous release read one alert
/// address from the mail settings; for one release the first recipient is copied there on every save
/// (and by the first save of the mail settings), so a rollback finds an address instead of silence.
/// It is written in the same save as the list, so the two cannot disagree. When no mail settings row
/// exists yet there is nothing to mirror into.
/// </para>
/// <para>
/// <b>The audit entry names the count and never the addresses.</b> The journal is append-only and
/// never deleted, and an address is personal data (rules/security.md item 8).
/// </para>
/// </remarks>
public sealed class SaveAlertRecipientsCommandHandler
{
    /// <summary>The module's database context, which owns the recipients.</summary>
    private readonly NotificationsDbContext _dbContext;

    /// <summary>The panel's append-only journal.</summary>
    private readonly NotificationsAuditJournal _journal;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="journal">The panel's append-only journal.</param>
    /// <param name="clock">The panel's clock, which stamps new rows.</param>
    public SaveAlertRecipientsCommandHandler(
        NotificationsDbContext dbContext,
        NotificationsAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Saves the alert recipient list.</summary>
    /// <param name="command">The validated list.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Success with no value.</returns>
    public async Task<Result> HandleAsync(SaveAlertRecipientsCommand command, CancellationToken cancellationToken)
    {
        var wanted = command.Addresses
            .Select(address => { return address.Trim().ToLowerInvariant(); })
            .ToList();

        var stored = await _dbContext.AlertRecipients.ToListAsync(cancellationToken);
        var now = _clock.UtcNow;

        foreach (var row in stored.Where(row => { return !wanted.Contains(row.Address); }))
        {
            _dbContext.AlertRecipients.Remove(row);
        }

        for (var position = 0; position < wanted.Count; position++)
        {
            var row = stored.FirstOrDefault(candidate => { return candidate.Address == wanted[position]; });
            if (row is null)
            {
                _dbContext.AlertRecipients.Add(new AlertRecipient(Guid.NewGuid(), wanted[position], position, now));
            }
            else
            {
                row.MoveTo(position);
            }
        }

        var settings = await _dbContext.SmtpSettings
            .FirstOrDefaultAsync(row => row.Id == SmtpSettings.SingletonId, cancellationToken);
        settings?.MirrorFirstAlertRecipient(wanted.Count > 0 ? wanted[0] : null);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordRequestAsync(
            OutgoingMailAuditActions.AlertRecipientsSaved,
            wanted.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        return Result.Ok();
    }
}
