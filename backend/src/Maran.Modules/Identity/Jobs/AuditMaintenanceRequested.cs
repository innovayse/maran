namespace Maran.Modules.Identity.Jobs;

/// <summary>
/// The scheduled trigger for one maintenance tick over <c>identity.AuditEvents</c>. Carries no
/// parameters: what a tick does is decided by the stored retention policy, the time of its last daily
/// run and the injected clock, never by whoever scheduled it.
/// </summary>
/// <remarks>
/// A message rather than a timer callback, for the same reason
/// <c>Maran.Modules.Tasks.Jobs.TaskRetentionRequested</c> is: a pass that fails is visible in the same
/// place as every other failed message, and a second scheduling mechanism would be a second thing to
/// reason about for no gain (rules/architecture.md). It carries nothing secret and nothing personal,
/// so it is safe wherever the bus keeps it.
/// </remarks>
public sealed record AuditMaintenanceRequested;
