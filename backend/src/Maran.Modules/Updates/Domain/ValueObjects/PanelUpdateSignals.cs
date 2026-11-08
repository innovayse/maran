namespace Maran.Modules.Updates.Domain.ValueObjects;

/// <summary>
/// The two facts about an update request that can be read from the file system at one instant: whether
/// the panel's request marker is waiting for root, and whether root says an update holds the lock.
/// </summary>
/// <param name="MarkerAge">How long ago the request marker was created, or null when there is none.</param>
/// <param name="RunningFresh">Whether root's running record exists and is young enough to be believed.</param>
public sealed record PanelUpdateSignals(TimeSpan? MarkerAge, bool RunningFresh);
