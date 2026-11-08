using Maran.Modules.Mail.Domain.Enums;

namespace Maran.Modules.Mail.Common;

/// <summary>One alias, forwarder or catch-all as a screen shows it.</summary>
/// <param name="Id">The row's identity, the only identifier a request may name.</param>
/// <param name="AccountId">The account that owns the address.</param>
/// <param name="OwnerName">The owning account's name, resolved by the backend so the list needs no second call.</param>
/// <param name="MailDomainId">The domain the address lives in.</param>
/// <param name="Address">The address, <c>local@domain</c>, or <c>*@domain</c> for a catch-all.</param>
/// <param name="Kind">What the address is.</param>
/// <param name="KindName">The localized name of <paramref name="Kind"/>, produced by the backend.</param>
/// <param name="Targets">Where the mail goes, in the order the owner gave.</param>
/// <param name="CreatedAt">The instant the address was created.</param>
public sealed record MailAliasDto(
    Guid Id,
    Guid AccountId,
    string OwnerName,
    Guid MailDomainId,
    string Address,
    MailAliasKind Kind,
    string KindName,
    IReadOnlyList<string> Targets,
    DateTimeOffset CreatedAt);
