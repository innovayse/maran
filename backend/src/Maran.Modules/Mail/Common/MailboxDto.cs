using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;

namespace Maran.Modules.Mail.Common;

/// <summary>Outward view of a <see cref="Mailbox"/>: everything a screen shows about one.</summary>
/// <remarks>
/// It carries no password and no hash, and there is nothing for it to carry one FROM that it may read:
/// the row holds only a hash, and no member of this type is built from it
/// (<c>MailboxDtoSecretTests</c> states that by reflection). A password is accepted by the create and
/// change-password commands and never returned by anything.
/// </remarks>
/// <param name="Id">The mailbox's identity, and the only identifier a request may name.</param>
/// <param name="AccountId">The account that owns this mailbox.</param>
/// <param name="OwnerName">
/// The owning account's name, resolved by the server so a list needs no second request to label its
/// rows. Empty when the account directory does not name the account for this caller.
/// </param>
/// <param name="MailDomainId">The mail domain the mailbox lives in.</param>
/// <param name="Address">
/// The full address, <c>localpart@domain</c>, which is also the login a mail client types.
/// </param>
/// <param name="QuotaMib">The size limit, in MiB.</param>
/// <param name="UsedMib">
/// How much of the quota the mailbox uses, in MiB rounded up, or <c>null</c> when the mail host has not
/// reported it (the host could not be asked, or has no reading for this mailbox yet).
/// </param>
/// <param name="SendLimitPerHour">The most messages the mailbox may send in an hour.</param>
/// <param name="Status">
/// Whether the mailbox accepts mail and logins; a reading derived from the owner's switch and the account's state.
/// </param>
/// <param name="StatusName">The status in the caller's language, produced by the panel.</param>
/// <param name="Enabled">
/// The owner's switch as stored, which <paramref name="Status"/> does not show while the account is
/// suspended: a screen that changes a mailbox's limits sends this back, so editing a suspended account's
/// mailbox never flips the owner's choice.
/// </param>
/// <param name="CreatedAt">The instant the mailbox was created.</param>
public sealed record MailboxDto(
    Guid Id,
    Guid AccountId,
    string OwnerName,
    Guid MailDomainId,
    string Address,
    int QuotaMib,
    long? UsedMib,
    int SendLimitPerHour,
    MailboxStatus Status,
    string StatusName,
    bool Enabled,
    DateTimeOffset CreatedAt);
