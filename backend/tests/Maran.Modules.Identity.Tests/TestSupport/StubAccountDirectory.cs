using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

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
/// <para>
/// Like the real directory it can be asked on behalf of a caller (<see cref="ActAs"/>), and like the
/// real directory it answers the two tenancy questions separately: <see cref="FindAsync"/> (which accounts
/// a caller's tenant data may touch) answers a reseller nothing, while <see cref="FindForViewAsAsync"/>
/// (which accounts a caller may start a view-as on) answers it the accounts registered with its reseller
/// id. Both are restated here as data (an account's owner) rather than as a query.
/// </para>
/// </remarks>
public sealed class StubAccountDirectory : IAccountDirectory
{
    /// <summary>The accounts this directory knows about, by id.</summary>
    private readonly Dictionary<Guid, AccountSnapshot> _accounts = [];

    /// <summary>The reseller that owns each account registered with an owner, by account id.</summary>
    private readonly Dictionary<Guid, Guid> _ownerResellers = [];

    /// <summary>The caller the directory answers for; null answers for everyone, as an administrator is answered.</summary>
    private ICurrentUser? _viewer;

    /// <summary>Registers an account with a system user name and an FTPS-login allowance.</summary>
    /// <param name="accountId">The account's identity.</param>
    /// <param name="username">The account's Linux system user name.</param>
    /// <param name="maxFtpUsers">How many FTPS logins the account's plan allows.</param>
    /// <param name="state">Whether the account is in service or suspended.</param>
    /// <param name="ownerLanguage">The owner's language, which an invitation for the account is written in.</param>
    /// <param name="resellerId">The reseller that owns the account, or null for an administrator's.</param>
    /// <returns>The snapshot that was registered, so a test can assert against the same values.</returns>
    public AccountSnapshot Add(
        Guid accountId,
        string username,
        int maxFtpUsers,
        AccountState state = AccountState.Active,
        string ownerLanguage = "en",
        Guid? resellerId = null)
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
            OwnerEmail: $"{username}@example.com",
            OwnerLanguage: ownerLanguage);

        _accounts[accountId] = snapshot;
        if (resellerId is { } owner)
        {
            _ownerResellers[accountId] = owner;
        }

        return snapshot;
    }

    /// <summary>Makes the directory answer on behalf of <paramref name="viewer"/>, as the real one does per request.</summary>
    /// <param name="viewer">The caller whose scope narrows the answers.</param>
    public void ActAs(ICurrentUser viewer)
    {
        _viewer = viewer;
    }

    /// <inheritdoc />
    public Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken)
    {
        // Tenant data: a reseller's touches no account, every other caller this double models sees all.
        var seen = _viewer?.Caller is not ResellerCaller;
        return Task.FromResult(seen && _accounts.TryGetValue(accountId, out var snapshot) ? snapshot : null);
    }

    /// <inheritdoc />
    public Task<AccountSnapshot?> FindForViewAsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return Task.FromResult(
            _accounts.TryGetValue(accountId, out var snapshot) && MayViewAs(accountId) ? snapshot : null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AccountSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
        // Unscoped, exactly as the contract says the real one is: every snapshot this double was given.
        return Task.FromResult<IReadOnlyList<AccountSnapshot>>([.. _accounts.Values]);
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

    /// <summary>
    /// Whether the directory's caller may start a view-as on an account: an administrator (or no caller
    /// set) any account, a reseller only an account registered with its own reseller id, anyone else none.
    /// </summary>
    /// <param name="accountId">The account.</param>
    /// <returns>True when the account is in the caller's view-as scope.</returns>
    private bool MayViewAs(Guid accountId)
    {
        return _viewer?.Caller switch
        {
            null => true,
            AdministratorCaller => true,
            ResellerCaller reseller => _ownerResellers.TryGetValue(accountId, out var owner)
                && owner == reseller.ResellerId,
            _ => false,
        };
    }

    /// <inheritdoc />
    public Task<AccountSystemUser?> FindSystemUserAsync(Guid accountId, CancellationToken cancellationToken)
    {
        // Refused rather than answered: only unattended certificate renewal asks this question.
        throw new NotSupportedException("These tests read no account's system user unscoped.");
    }
}
