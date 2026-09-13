namespace Maran.Modules.Tasks.Jobs;

/// <summary>
/// The scheduled trigger for one retention pass over <c>tasks.PanelTasks</c>. Carries no parameters:
/// the pass purges whatever has aged out, and "aged out" is decided by
/// <see cref="TaskRetentionHandler.RetentionWindow"/> and the injected clock, never by whoever
/// scheduled it.
/// </summary>
/// <remarks>
/// A message rather than a timer callback, for the same reason
/// <c>Maran.Modules.Ssl.Jobs.CertificateRenewalRequested</c> is: the panel's message bus is durable,
/// so a pass scheduled while the panel was restarting still runs, and a pass that fails is visible in
/// the same place as every other failed message. Adding a second scheduling mechanism — a hosted
/// service with its own timer running the delete itself — would be a second thing to reason about for
/// no gain (rules/architecture.md).
/// </remarks>
public sealed record TaskRetentionRequested;
