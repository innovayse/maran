namespace Maran.Modules.Backups.Queries.ListBackups;

/// <summary>
/// The public names the backup history sorts by — the <c>sort</c> parameter's vocabulary, written
/// once and read by <see cref="ListBackupsSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class BackupSortFields
{
    /// <summary>When the run began.</summary>
    public const string StartedAt = "startedAt";

    /// <summary>The artifact's size in bytes.</summary>
    public const string Size = "size";
}
