namespace Maran.Modules.ServerMigration.Queries.ListDiscoveredAccounts;

/// <summary>
/// The public names the discovered accounts list sorts by — the <c>sort</c> parameter's vocabulary, written once and
/// read by <see cref="ListDiscoveredAccountsSorts.Map"/> and the tests.
/// </summary>
public static class DiscoveredAccountSortFields
{
    /// <summary>The account's login on the source.</summary>
    public const string Name = "name";

    /// <summary>The space the home directory uses.</summary>
    public const string Disk = "disk";

    /// <summary>How many sites the account serves.</summary>
    public const string Sites = "sites";

    /// <summary>How many databases the account owns.</summary>
    public const string Databases = "databases";
}
