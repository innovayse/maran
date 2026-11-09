using Maran.Sdk.Contracts;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>Builds the account snapshots the Sites tests hand to <see cref="StubAccountDirectory"/>.</summary>
public static class Snapshots
{
    /// <summary>An active account on a generous plan.</summary>
    /// <param name="accountId">The account's id.</param>
    /// <param name="username">The account's system user name.</param>
    /// <returns>The snapshot.</returns>
    public static AccountSnapshot Account(Guid accountId, string username)
    {
        return new AccountSnapshot(
            accountId,
            username,
            MaxSites: 5,
            MaxDatabases: 2,
            MaxSftpUsers: 3,
            MaxCronEntries: 7,
            MaxPhpWorkersPerPool: 10,
            DiskQuotaMb: 1_024,
            AccountState.Active);
    }
}
