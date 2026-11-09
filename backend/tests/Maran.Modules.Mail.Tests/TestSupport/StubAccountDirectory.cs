using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Mail.Tests.TestSupport;

/// <summary>
/// An <see cref="IAccountDirectory"/> answering from a table the test fills, and nothing else. A missing
/// account answers <c>null</c>, which is exactly how the real, tenant-scoped window answers for an account
/// the caller may not see.
/// </summary>
public sealed class StubAccountDirectory : IAccountDirectory
{
    /// <summary>The accounts this stub knows.</summary>
    private readonly Dictionary<Guid, AccountSnapshot> _accounts = [];

    /// <summary>Adds an account with the given mail limits.</summary>
    /// <param name="accountId">The account's identity.</param>
    /// <param name="username">The account's system user name.</param>
    /// <param name="limits">The plan's mail limits, or <c>null</c> for <see cref="MailLimitsSnapshot.Default"/>.</param>
    /// <param name="state">Whether the account is active or suspended.</param>
    /// <returns>The snapshot the stub now answers with.</returns>
    public AccountSnapshot Add(
        Guid accountId,
        string username,
        MailLimitsSnapshot? limits = null,
        AccountState state = AccountState.Active)
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
            State: state)
        {
            MailLimits = limits ?? MailLimitsSnapshot.Default,
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
        throw new NotSupportedException("Mail tests start no view-as session.");
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
        throw new NotSupportedException("Mail searches no account names.");
    }

    /// <inheritdoc />
    public Task<AccountSystemUser?> FindSystemUserAsync(Guid accountId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("These tests read no account's system user unscoped.");
    }
}
