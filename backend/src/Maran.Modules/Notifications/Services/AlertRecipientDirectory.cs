using Maran.Modules.Notifications.Persistence;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Notifications.Services;

/// <summary>
/// This module's implementation of <see cref="IAlertRecipientDirectory"/>: the addresses operator
/// alerts are mailed to, and nothing else this module holds.
/// </summary>
/// <remarks>
/// <para>
/// <b>It reads the recipients' table and never the mail settings.</b> The settings row also holds a
/// password; what crosses the module boundary is a list of addresses, so returning anything wider
/// would hand every consumer of the Sdk contract a working credential for the operator's mail
/// provider (rules/security.md item 8).
/// </para>
/// <para>
/// <b>No cache.</b> The caller is the alert evaluator, once a minute at most, and the list is at most
/// <c>AlertRecipient.MaxCount</c> short rows. A cache would need invalidating on every save for no
/// measurable gain, and a stale one would mail an address the administrator had just removed.
/// </para>
/// </remarks>
public sealed class AlertRecipientDirectory : IAlertRecipientDirectory
{
    /// <summary>The module's database context, which owns the recipients.</summary>
    private readonly NotificationsDbContext _dbContext;

    /// <summary>Creates the directory.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public AlertRecipientDirectory(NotificationsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetAlertRecipientsAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.AlertRecipients
            .AsNoTracking()
            .OrderBy(recipient => recipient.Position)
            .Select(recipient => recipient.Address)
            .ToListAsync(cancellationToken);
    }
}
