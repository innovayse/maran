namespace Maran.Modules.ServerMigration.Queries.ListDiscoveredAccounts;

/// <summary>
/// The values the <c>has</c> filter of the discovered accounts list accepts: narrow the list to accounts that have
/// at least one of the named thing. Written once for the handler, the tests and the interface's contract.
/// </summary>
public static class DiscoveredAccountHasFilters
{
    /// <summary>Accounts that serve at least one site.</summary>
    public const string Sites = "sites";

    /// <summary>Accounts that own at least one database.</summary>
    public const string Databases = "databases";

    /// <summary>Accounts that have at least one scheduled job.</summary>
    public const string Cron = "cron";

    /// <summary>Accounts that hold at least one mailbox.</summary>
    public const string Mail = "mail";
}
