namespace Maran.Modules.Backups.Models;

/// <summary>What one pruning loop did: how many backups it removed and whether the agent stopped it.</summary>
/// <param name="Pruned">How many backups (archive and row) were removed.</param>
/// <param name="Stopped">
/// Whether the loop ended on an agent refusal rather than by running out of backups; a refusing agent
/// refuses every deletion, so the caller does not go on to a second loop.
/// </param>
public sealed record PruneResult(int Pruned, bool Stopped);
