using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Models;

namespace Maran.Modules.Marketplace.Jobs;

/// <summary>
/// Asks the panel to carry out an install, update, remove or rollback whose request was accepted: the task
/// exists, the module's operation gate is held and the request is audited. The password the administrator
/// typed is NOT in it, and neither is anything from the marketplace; it names the work and nothing else.
/// </summary>
/// <remarks>
/// A message rather than a task started in the request, for the reason <c>BackupRunRequested</c> is: the
/// panel's bus runs it after the 202 has gone out, on its own shutdown-aware worker, and a failure shows in
/// the same place as every other failed message. It travels the local, in-memory queue and carries no
/// secret, so nothing about it is written to the message store (rules/csharp.md "Queue durability is
/// decided per message").
/// </remarks>
/// <param name="TaskId">The task that carries the operation's progress and result.</param>
/// <param name="Operation">Which operation.</param>
/// <param name="ModuleId">The module id.</param>
/// <param name="Version">The version to install, or the installed version a remove or rollback acts on.</param>
/// <param name="DisplayName">The catalogue's name for the module, for the ledger entry an install or update writes; null for a remove or rollback.</param>
/// <param name="Claim">The held operation gate, released when the run ends.</param>
/// <param name="UserId">The administrator who asked.</param>
/// <param name="IpAddress">The administrator's address, for the audit entry written if the run fails.</param>
/// <param name="UserAgent">The administrator's user agent, for the same entry.</param>
public sealed record ModuleOperationRequested(
    Guid TaskId,
    MarketplaceOperation Operation,
    string ModuleId,
    string? Version,
    string? DisplayName,
    ModuleOperationClaim Claim,
    Guid UserId,
    string IpAddress,
    string UserAgent);
