namespace Maran.Modules.ServerMigration.Queries.ListMigrationJobs;

/// <summary>The names the job list may be sorted by.</summary>
public static class MigrationJobSortFields
{
    /// <summary>By label.</summary>
    public const string Name = "name";

    /// <summary>By state.</summary>
    public const string State = "state";

    /// <summary>By creation time.</summary>
    public const string CreatedAt = "createdAt";
}
