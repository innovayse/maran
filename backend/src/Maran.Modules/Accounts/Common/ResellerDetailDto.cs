using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;

namespace Maran.Modules.Accounts.Common;

/// <summary>
/// Outward view of one <see cref="Reseller"/>: everything <see cref="ResellerDto"/> says plus its
/// language and the envelope it was sold. It is also what a reseller reads about itself
/// (<c>GET /api/v1/reseller/me</c>), so an administrator and the reseller see one shape.
/// </summary>
/// <param name="Id">The reseller's identity.</param>
/// <param name="Name">The reseller's unique short name, which is also its login name.</param>
/// <param name="ContactEmail">The address its login's invitation and password-reset mails go to.</param>
/// <param name="Status">The reseller's lifecycle state.</param>
/// <param name="StatusName">The state's name resolved server-side for the request's culture.</param>
/// <param name="CreatedAt">The instant the reseller was created.</param>
/// <param name="AccountCount">How many accounts, in any status, the reseller owns right now.</param>
/// <param name="PlanCount">How many plans the reseller owns right now.</param>
/// <param name="DiskAllocatedMb">The disk, in megabytes, promised to the accounts it owns.</param>
/// <param name="Language">The reseller's language, one of the panel's supported languages.</param>
/// <param name="Envelope">What the administrator sold the reseller; it may sit below the usage when it was lowered.</param>
public sealed record ResellerDetailDto(
    Guid Id,
    string Name,
    string ContactEmail,
    ResellerStatus Status,
    string StatusName,
    DateTimeOffset CreatedAt,
    int AccountCount,
    int PlanCount,
    long DiskAllocatedMb,
    string Language,
    ResellerEnvelopeDto Envelope);
