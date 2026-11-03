using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;

namespace Maran.Modules.Accounts.Common;

/// <summary>Outward view of a <see cref="Reseller"/> in a list: who it is, where it stands and what it uses.</summary>
/// <param name="Id">The reseller's identity.</param>
/// <param name="Name">The reseller's unique short name, which is also its login name.</param>
/// <param name="ContactEmail">The address its login's invitation and password-reset mails go to.</param>
/// <param name="Status">The reseller's lifecycle state.</param>
/// <param name="StatusName">The state's name resolved server-side for the request's culture.</param>
/// <param name="CreatedAt">The instant the reseller was created.</param>
/// <param name="AccountCount">How many accounts, in any status, the reseller owns right now.</param>
/// <param name="PlanCount">How many plans the reseller owns right now.</param>
/// <param name="DiskAllocatedMb">
/// The disk, in megabytes, promised to the accounts it owns: the sum of their plans' disk quotas, which
/// is the figure the envelope's disk pool is compared with.
/// </param>
public sealed record ResellerDto(
    Guid Id,
    string Name,
    string ContactEmail,
    ResellerStatus Status,
    string StatusName,
    DateTimeOffset CreatedAt,
    int AccountCount,
    int PlanCount,
    long DiskAllocatedMb);
