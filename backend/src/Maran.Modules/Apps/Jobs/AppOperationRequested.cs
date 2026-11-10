using Maran.Modules.Apps.Domain.Enums;

namespace Maran.Modules.Apps.Jobs;

/// <summary>
/// Asks the panel to carry out an install or a removal whose request was accepted: the task exists, the
/// ledger row says it is running and the request is audited. It names the work and nothing else.
/// </summary>
/// <remarks>
/// A message rather than work started in the request, for the reason <c>BackupRunRequested</c> is: the
/// panel's bus runs it after the 202 has gone out, on its own shutdown-aware worker, and a package manager
/// fetching on a slow mirror can take minutes. It carries no secret and travels the local, in-memory queue,
/// so nothing about it is written to the message store (rules/csharp.md "Queue durability is decided per
/// message"). A panel that stops while it runs leaves a task and a ledger row that the start-up passes
/// close (<c>StartupTaskReconciler</c>, <c>StartupAppReconciler</c>).
/// </remarks>
/// <param name="TaskId">The task that carries the operation's progress and result.</param>
/// <param name="Operation">Which operation.</param>
/// <param name="AppId">The catalogue id of the app, already parsed once by the request that accepted it.</param>
public sealed record AppOperationRequested(Guid TaskId, AppOperation Operation, string AppId);
