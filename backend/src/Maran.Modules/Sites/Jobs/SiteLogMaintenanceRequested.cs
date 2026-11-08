namespace Maran.Modules.Sites.Jobs;

/// <summary>
/// The scheduled trigger for one hourly pass over every site's logs: read what each access log counted
/// since the last pass, then remove the rotated archives beyond each site's own retention, then purge
/// traffic rows older than the panel keeps (issue #152). Carries no parameters: the pass works from the
/// stored rows, never from whoever scheduled it.
/// </summary>
/// <remarks>
/// A message rather than a timer callback that works inline, for the reason the other modules' scheduled
/// passes are: the panel's message bus is durable, so a pass scheduled while the panel was restarting
/// still runs, and a pass that fails is visible where every other failed message is. It carries nothing at
/// all, so there is no secret for durability to leave at rest.
/// </remarks>
public sealed record SiteLogMaintenanceRequested;
