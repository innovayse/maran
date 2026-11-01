namespace Maran.Modules.Monitoring.Models;

/// <summary>
/// One round's answer to "how close is the root filesystem to the agent's free-space floor": what an
/// unprivileged writer may still add, beside the floor below which the agent refuses new accounts,
/// backups, restores and uploads (issue #148).
/// </summary>
/// <param name="AvailableBytes">
/// Bytes any non-root writer may still add to the root filesystem — <c>df</c>'s <c>Avail</c>.
/// </param>
/// <param name="FloorBytes">The agent's free-space floor, in bytes, as it reported it.</param>
public sealed record FreeSpaceFloorReading(ulong AvailableBytes, ulong FloorBytes);
