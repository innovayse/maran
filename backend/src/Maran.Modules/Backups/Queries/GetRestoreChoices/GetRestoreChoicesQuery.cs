namespace Maran.Modules.Backups.Queries.GetRestoreChoices;

/// <summary>Reads the databases and sites a restore of one backup could be narrowed to.</summary>
/// <param name="BackupId">The backup about to be restored. Another customer's answers not-found.</param>
public sealed record GetRestoreChoicesQuery(Guid BackupId);
