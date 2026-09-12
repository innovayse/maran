using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// An <see cref="IAccountDirectory"/> that knows one account, so a handler can read a system user
/// name and a plan allowance with no Accounts module in the process.
/// </summary>
/// <remarks>
/// It answers <c>null</c> for every other id, which is the same answer the real directory gives for
/// an account belonging to another tenant.
/// </remarks>
public sealed class OneAccountDirectory : IAccountDirectory
{
    /// <summary>The only account this directory knows.</summary>
    private readonly AccountSnapshot _snapshot;

    /// <summary>Creates the directory.</summary>
    /// <param name="accountId">The account's identity.</param>
    /// <param name="username">The account's Linux system user name.</param>
    /// <param name="maxFtpUsers">How many FTPS logins the account's plan allows.</param>
    public OneAccountDirectory(Guid accountId, string username, int maxFtpUsers)
    {
        _snapshot = new AccountSnapshot(
            accountId,
            username,
            MaxSites: 1,
            MaxDatabases: 1,
            MaxSftpUsers: 1,
            MaxCronEntries: 1,
            MaxPhpWorkersPerPool: 1,
            DiskQuotaMb: 1024,
            MaxFtpUsers: maxFtpUsers);
    }

    /// <inheritdoc />
    public Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return Task.FromResult(accountId == _snapshot.Id ? _snapshot : null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AccountSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<AccountSnapshot>>([_snapshot]);
    }
}
