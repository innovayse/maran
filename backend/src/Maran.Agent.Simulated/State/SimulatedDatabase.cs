namespace Maran.Agent.Simulated.State;

/// <summary>One database and its dedicated user as the simulated server holds them.</summary>
/// <param name="Account">System username of the owning account.</param>
/// <param name="DatabaseSuffix">The database name suffix the customer chose.</param>
/// <param name="UserSuffix">The dedicated user's name suffix, chosen independently of the database's.</param>
/// <param name="SizeBytes">The on-disk size the server reports for the database.</param>
public sealed record SimulatedDatabase(string Account, string DatabaseSuffix, string UserSuffix, ulong SizeBytes)
{
    /// <summary>The database name as the server holds it: the account prefix, then the suffix.</summary>
    public string FullDatabaseName
    {
        get { return $"{Account}_{DatabaseSuffix}"; }
    }

    /// <summary>The user name as the server holds it: the account prefix, then the suffix.</summary>
    public string FullUserName
    {
        get { return $"{Account}_{UserSuffix}"; }
    }
}
