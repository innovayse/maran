namespace Maran.Modules.ServerMigration.Queries.ListMigrationJobAccounts;

/// <summary>The names the account list may be sorted by.</summary>
public static class MigrationJobAccountSortFields
{
    /// <summary>By account name.</summary>
    public const string Name = "name";

    /// <summary>By state.</summary>
    public const string State = "state";

    /// <summary>By size of the home on the source.</summary>
    public const string Size = "size";
}
