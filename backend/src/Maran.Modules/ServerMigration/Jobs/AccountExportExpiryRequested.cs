namespace Maran.Modules.ServerMigration.Jobs;

/// <summary>
/// The scheduled trigger for one pass that removes the exports whose retention has ended (issue #127, decision D11).
/// Carries no parameters: what has expired is decided by each row's
/// own <c>ExpiresAt</c> and the injected clock, never by whoever scheduled the pass.
/// </summary>
/// <remarks>
/// A message and not a timer callback, for the reason <c>TaskRetentionRequested</c> gives: the bus is durable, so a
/// pass scheduled while the panel was restarting still runs, and a pass that fails is visible with every other
/// failed message.
/// </remarks>
public sealed record AccountExportExpiryRequested;
