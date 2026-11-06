namespace Maran.Modules.Idempotency.Jobs;

/// <summary>
/// The scheduled trigger for one purge of expired idempotency records. Carries no parameters: what has
/// expired is decided by each row's own <c>ExpiresAt</c> and the injected clock, never by whoever
/// scheduled the pass.
/// </summary>
/// <remarks>
/// A message rather than a timer callback, for the reason <c>TaskRetentionRequested</c> is one: the
/// panel's bus is durable, so a pass scheduled while the panel was restarting still runs, and a pass
/// that fails is visible where every other failed message is. The Host owns the hourly cadence.
/// </remarks>
public sealed record ExpiredRecordPurgeRequested;
