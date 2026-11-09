using Maran.Sdk.Contracts;

namespace Maran.Modules.Files.Tests.TestSupport;

/// <summary>Builds the account snapshots the tests use.</summary>
public static class FilesTestAccounts
{
    /// <summary>Builds a snapshot with the given state.</summary>
    /// <param name="id">The account id.</param>
    /// <param name="username">The system user name.</param>
    /// <param name="state">Active or suspended.</param>
    /// <returns>The snapshot.</returns>
    public static AccountSnapshot Account(Guid id, string username, AccountState state)
    {
        return new AccountSnapshot(id, username, 1, 1, 1, 1, 1, 1024, state);
    }
}
