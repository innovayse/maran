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
    /// <param name="maxSites">How many sites the account's plan allows.</param>
    /// <param name="maxDatabases">How many databases the account's plan allows.</param>
    /// <param name="maxSftpUsers">How many SFTP logins the account's plan allows.</param>
    /// <remarks>
    /// Every allowance but the FTPS one defaults to 1 and is named by a caller that cares about it.
    /// One per limit rather than one shared number, because a race test's whole subject is ONE
    /// allowance and a shared figure would make a test that measured the wrong one look correct.
    /// </remarks>
    public OneAccountDirectory(
        Guid accountId,
        string username,
        int maxFtpUsers = 1,
        int maxSites = 1,
        int maxDatabases = 1,
        int maxSftpUsers = 1)
    {
        _snapshot = new AccountSnapshot(
            accountId,
            username,
            MaxSites: maxSites,
            MaxDatabases: maxDatabases,
            MaxSftpUsers: maxSftpUsers,
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
