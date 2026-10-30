namespace Maran.Modules.Backups.Queries.ListBackupDestinations;

/// <summary>
/// The public names the destinations list sorts by — the <c>sort</c> parameter's vocabulary, written
/// once and read by <see cref="ListBackupDestinationsSorts.Map"/> and the tests.
/// </summary>
public static class BackupDestinationSortFields
{
    /// <summary>The destination's name as the screen shows it.</summary>
    public const string Name = "name";

    /// <summary>When the panel first recorded the destination.</summary>
    public const string CreatedAt = "createdAt";
}
