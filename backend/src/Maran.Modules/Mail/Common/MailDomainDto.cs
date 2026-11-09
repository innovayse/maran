using Maran.Modules.Mail.Domain.Enums;

namespace Maran.Modules.Mail.Common;

/// <summary>A mail domain as the panel lists it.</summary>
/// <param name="Id">The mail domain's identifier.</param>
/// <param name="AccountId">The owning account.</param>
/// <param name="OwnerName">The owning account's display name, resolved in one batched lookup per page.</param>
/// <param name="Name">The domain, lower-case.</param>
/// <param name="Status">Whether the domain is serving or its account is suspended; a reading, never stored.</param>
/// <param name="StatusName">The localized name of <paramref name="Status"/>.</param>
/// <param name="DkimSelector">The selector of the key signing the domain's outgoing mail, or empty when none is active.</param>
/// <param name="MailboxCount">How many mailboxes the domain holds.</param>
/// <param name="CreatedAt">When the domain was added.</param>
public sealed record MailDomainDto(
    Guid Id,
    Guid AccountId,
    string OwnerName,
    string Name,
    MailDomainStatus Status,
    string StatusName,
    string DkimSelector,
    int MailboxCount,
    DateTimeOffset CreatedAt);
