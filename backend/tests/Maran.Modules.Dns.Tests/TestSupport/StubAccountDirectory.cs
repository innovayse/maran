using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>
/// An <see cref="IAccountDirectory"/> answering with exactly the snapshots a test registered. An unregistered
/// id answers <c>null</c>, which is the answer the real directory gives for an account that belongs to
/// another tenant, so a test cannot depend on telling the two apart.
/// </summary>
public sealed class StubAccountDirectory : IAccountDirectory
{
    /// <summary>The accounts this directory knows about, by id.</summary>
    private readonly Dictionary<Guid, AccountSnapshot> _accounts = [];

    /// <summary>Registers an account with a system user name and a DNS zone allowance.</summary>
    /// <param name="accountId">The account's identity.</param>
    /// <param name="username">The account's system user name.</param>
    /// <param name="maxDnsZones">How many zones the account's plan allows.</param>
    /// <returns>The snapshot that was registered.</returns>
    public AccountSnapshot Add(Guid accountId, string username, int maxDnsZones)
    {
        var snapshot = new AccountSnapshot(
            accountId,
            username,
            MaxSites: 1,
            MaxDatabases: 1,
            MaxSftpUsers: 1,
            MaxCronEntries: 1,
            MaxPhpWorkersPerPool: 1,
            DiskQuotaMb: 1024,
            State: AccountState.Active)
        {
            MaxDnsZones = maxDnsZones,
        };

        _accounts[accountId] = snapshot;
        return snapshot;
    }

    /// <inheritdoc />
    public Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_accounts.TryGetValue(accountId, out var snapshot) ? snapshot : null);
    }

    /// <inheritdoc />
    public Task<AccountSnapshot?> FindForViewAsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Dns tests start no view-as session.");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AccountSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<AccountSnapshot>>(_accounts.Values.ToList());
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<Guid, string>> NamesOfAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, string> names = accountIds
            .Distinct()
            .Where(id =>
            {
                return _accounts.ContainsKey(id);
            })
            .ToDictionary(
                id =>
                {
                    return id;
                },
                id =>
                {
                    return _accounts[id].Username;
                });
        return Task.FromResult(names);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> FindIdsByNameAsync(string search, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Dns searches no account names.");
    }

    /// <inheritdoc />
    public Task<AccountSystemUser?> FindSystemUserAsync(Guid accountId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("These tests read no account's system user unscoped.");
    }
}
