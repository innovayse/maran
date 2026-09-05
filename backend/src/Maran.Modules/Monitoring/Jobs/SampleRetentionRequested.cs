namespace Maran.Modules.Monitoring.Jobs;

/// <summary>
/// The scheduled trigger for one retention pass over <c>monitoring.Samples</c>. Carries no
/// parameters: the pass deletes whatever has aged out, and "aged out" is decided by
/// <see cref="SampleRetentionHandler.RetentionWindow"/> and the injected clock, never by whoever
/// scheduled it.
/// </summary>
/// <remarks>
/// A message rather than a timer callback that deletes inline, for the reason the Tasks module's
/// equivalent is one: the panel's message bus is durable, so a pass scheduled while the panel was
/// restarting still runs, and a pass that fails is visible in the same place as every other failed
/// message. Adding a second scheduling mechanism would be a second thing to reason about for no gain
/// (rules/architecture.md).
///
/// It is unrelated to <c>SendMailRequested</c>'s local, non-durable queue: this message carries
/// nothing at all, so there is no secret for durability to leave at rest.
/// </remarks>
public sealed record SampleRetentionRequested;
