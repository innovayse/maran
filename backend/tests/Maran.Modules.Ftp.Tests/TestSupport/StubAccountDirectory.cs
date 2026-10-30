using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ftp.Tests.TestSupport;

/// <summary>
/// An <see cref="IAccountDirectory"/> answering with exactly the snapshots a test registered, so the
/// handlers' two questions — what is this account called on the host, and how many FTPS logins may
/// it hold — have decidable answers with no Accounts module in the process.
/// </summary>
/// <remarks>
/// <see cref="FindAsync"/> answers <c>null</c> for anything unregistered, which is the same answer
/// the real directory gives for an account belonging to another tenant. The two are deliberately
/// indistinguishable there and are deliberately indistinguishable here, so a test cannot accidentally
/// depend on the difference.
/// </remarks>
public sealed class StubAccountDirectory : IAccountDirectory
{
    /// <summary>The accounts this directory knows about, by id.</summary>
    private readonly Dictionary<Guid, AccountSnapshot> _accounts = [];

    /// <summary>Each batch of ids <see cref="NamesOfAsync"/> was asked for, in order.</summary>
    public List<IReadOnlyCollection<Guid>> NameLookups { get; } = [];

    /// <summary>Registers an account with a system user name and an FTPS-login allowance.</summary>
    /// <param name="accountId">The account's identity.</param>
    /// <param name="username">The account's Linux system user name.</param>
    /// <param name="maxFtpUsers">How many FTPS logins the account's plan allows.</param>
    /// <returns>The snapshot that was registered, so a test can assert against the same values.</returns>
    public AccountSnapshot Add(Guid accountId, string username, int maxFtpUsers)
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
            State: AccountState.Active,
            MaxFtpUsers: maxFtpUsers);

        _accounts[accountId] = snapshot;
        return snapshot;
    }

    /// <inheritdoc />
    public Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_accounts.TryGetValue(accountId, out var snapshot) ? snapshot : null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AccountSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<AccountSnapshot>>(_accounts.Values.ToList());
    }

    /// <inheritdoc />
    /// <remarks>
    /// Answers only for the snapshots it was given — an id it does not know is absent, which is how
    /// the real directory answers for an account that no longer exists or belongs to another tenant.
    /// </remarks>
    public Task<IReadOnlyDictionary<Guid, string>> NamesOfAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        NameLookups.Add(accountIds);
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
        // Refused: nothing in this module searches its rows by owner name.
        throw new NotSupportedException("Ftp searches no account names.");
    }
}
