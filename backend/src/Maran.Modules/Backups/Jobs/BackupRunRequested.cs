namespace Maran.Modules.Backups.Jobs;

/// <summary>
/// The scheduled trigger for one sweep over the panel's backup schedules. Carries no parameters:
/// which schedules are due is decided by each schedule and the injected clock, never by whoever
/// scheduled the sweep.
/// </summary>
/// <remarks>
/// A message rather than a timer callback, for the reason <c>TaskRetentionRequested</c> and
/// <c>CertificateRenewalRequested</c> are: the panel's message bus is durable, so a sweep queued
/// while the panel was restarting still runs, and one that fails is visible in the same place as
/// every other failed message. A second scheduling mechanism — a hosted service with its own timer
/// doing the work itself — would be another thing to reason about for no gain
/// (rules/architecture.md).
///
/// It carries no secret and travels the panel's local, in-memory queue, so nothing about it is
/// written to the message store (rules/csharp.md "Queue durability is decided per message").
/// </remarks>
public sealed record BackupRunRequested;
