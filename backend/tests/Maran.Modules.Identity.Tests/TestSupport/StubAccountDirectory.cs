using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// An <see cref="IAccountDirectory"/> answering with exactly the snapshots a test registered, so
/// <c>ResendInvitationCommandHandler</c>'s one question — what is this account's owner called and
/// what is their contact address — has a decidable answer with no Accounts module in the process.
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

    /// <summary>Registers an account with a system user name and an FTPS-login allowance.</summary>
    /// <param name="accountId">The account's identity.</param>
    /// <param name="username">The account's Linux system user name.</param>
    /// <param name="maxFtpUsers">How many FTPS logins the account's plan allows.</param>
    /// <param name="state">Whether the account is in service or suspended.</param>
    /// <returns>The snapshot that was registered, so a test can assert against the same values.</returns>
    public AccountSnapshot Add(Guid accountId, string username, int maxFtpUsers, AccountState state = AccountState.Active)
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
            State: state,
            MaxFtpUsers: maxFtpUsers,
            OwnerEmail: $"{username}@example.com");

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
    public Task<IReadOnlyDictionary<Guid, string>> NamesOfAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        // Refused rather than answered: nothing in this module labels rows with account names, and
        // the tenant scope the real implementation applies is the part a double cannot honestly model.
        throw new NotSupportedException("Identity reads no account names.");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> FindIdsByNameAsync(string search, CancellationToken cancellationToken)
    {
        // Refused for the same reason as NamesOfAsync: nothing in this module searches by owner.
        throw new NotSupportedException("Identity searches no account names.");
    }
}
