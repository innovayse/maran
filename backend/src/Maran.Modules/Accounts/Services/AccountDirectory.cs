using System.Diagnostics;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// The Accounts module's implementation of <see cref="IAccountDirectory"/>: the one place another
/// module may learn an account's system user name and its plan's allowances, without ever
/// referencing this module (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// <para>
/// For <see cref="FindAsync"/> the tenant scope is applied HERE, in the query, rather than left to
/// the calling module. A cross-module abstraction is the natural gap in a tenancy story: the
/// caller's own <c>DbContext</c> query filter cannot reach across into this schema, so if this type
/// answered every id it was handed, a customer could learn another tenant's system user name — the
/// name that addresses every agent operation — simply by guessing an account id.
/// </para>
/// <para>
/// <see cref="NamesOfAsync"/> and <see cref="FindIdsByNameAsync"/> apply the same scope, by the same
/// comparison against the caller's own token: a customer's answer can only ever name their own account.
/// </para>
/// <para>
/// A reseller's TENANT DATA touches no account: <see cref="Sees"/> answers false for it, so
/// <see cref="FindAsync"/> answers null, <see cref="NamesOfAsync"/> an empty set and
/// <see cref="FindIdsByNameAsync"/> no id, and a module that scopes its rows only through this window
/// (Cron keeps none of its own) shows a reseller nothing, even for an account it owns (issue #179,
/// decision D8). The reseller's own accounts are answered by <see cref="FindForViewAsAsync"/> alone,
/// for the one purpose of starting a view-as session: it adds <c>account.ResellerId == reseller.ResellerId</c>
/// to its query, so an account another reseller or the administrator owns answers exactly as an account
/// that does not exist. That predicate is stated here AND in <see cref="AccountsDbContext"/>'s query
/// filter, on purpose: a window a view-as start rests on should not depend on one mechanism that a
/// later edit could remove.
/// </para>
/// <para>
/// <see cref="ListAsync"/> is the deliberate exception and the interface says so: it answers with
/// every account on the host and applies no scope at all, bypassing the context's query filters
/// explicitly, because the host-wide view it exists for has no tenant to scope by. Its authorization
/// lives entirely at its caller's boundary.
/// </para>
/// <para>
/// <see cref="FindSystemUserAsync"/> is the second, narrower exception: one account's system user name
/// by id, with no scope, for unattended work that holds the id from its own row (certificate renewal).
/// It cannot list and returns nothing but the id and the name (issue #379).
/// </para>
/// </remarks>
public sealed class AccountDirectory : IAccountDirectory
{
    /// <summary>The Accounts module's database context; this module owns the accounts schema.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The authenticated principal, whose tenant scope bounds every answer.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the directory.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public AccountDirectory(AccountsDbContext dbContext, ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken)
    {
        // An administrator sees every account; a customer sees exactly the one they own; a reseller
        // none. The comparison is against the id the caller's own token carries, never against anything
        // on the request, so a forged or guessed account id cannot widen it.
        if (!Sees(accountId))
        {
            return null;
        }

        return await ReadSnapshotAsync(_dbContext.Accounts.AsNoTracking(), accountId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AccountSnapshot?> FindForViewAsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        // Who may START a view-as on an account: an administrator any, a reseller the accounts it owns,
        // every other case none. Decided from the caller's own token, as every other read here is.
        var accounts = _currentUser.Caller switch
        {
            AdministratorCaller => _dbContext.Accounts.AsNoTracking(),
            ResellerCaller reseller => WhereOwnedBy(_dbContext.Accounts.AsNoTracking(), reseller.ResellerId),
            CustomerCaller => null,
            ApiKeyCaller => null,
            AnonymousCaller => null,
            AccountlessCustomerCaller => null,
            _ => throw new UnreachableException("Caller is sealed to SharedKernel: six cases exist."),
        };

        return accounts is null ? null : await ReadSnapshotAsync(accounts, accountId, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// No tenant scope, by contract — see <see cref="IAccountDirectory.ListAsync"/>, which states
    /// the exposure and puts the whole authorization burden on the caller. There is deliberately no
    /// administrator check here to mirror <see cref="FindAsync"/>'s: this method runs from the
    /// administrator's host disk view and also runs from unattended work, where there
    /// is no principal at all and a check against one would silently answer "nothing" rather than
    /// fail — the failure mode <c>CertificateRenewalHandler</c> and <c>TaskRetentionHandler</c> both
    /// record. A check that answers emptiness instead of refusing is not a security control, it is
    /// a feature that stops working in the dark.
    /// </para>
    /// <para>
    /// The call bypasses <see cref="AccountsDbContext"/>'s query filters, on both accounts and plans,
    /// on purpose: the filters fail closed (an anonymous caller sees nothing) and this method is
    /// reached by unattended work with no principal, so a scoped read would silently answer an empty
    /// list. The bypass is explicit and carries its reason on the line, as every other one does.
    /// </para>
    /// <para>
    /// Every account is returned, suspended ones included. A suspended account's files are still on
    /// the disk and still counted by the agent, so omitting it would make the host view's rows sum
    /// to less than the disk actually holds — and the account most worth looking at, when somebody
    /// is looking at disk usage, is often exactly the one that was suspended for filling it.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<AccountSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // unattended runs have no principal; the fail-closed filters would answer nothing
        var accounts = _dbContext.Accounts.IgnoreQueryFilters().AsNoTracking();
        var plans = _dbContext.Plans.IgnoreQueryFilters().AsNoTracking();
#pragma warning restore RS0030

        return await ProjectSnapshots(accounts, plans).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, string>> NamesOfAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        // The same scope FindAsync applies, to a set instead of one id: a customer's answer can only
        // ever hold their own account and a reseller's nothing, whatever ids they managed to put in
        // front of the caller.
        var visible = accountIds.Where(Sees).Distinct().ToList();

        if (visible.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        // The selectors are delegates run after the read, not translated, so they are braced.
        return await _dbContext.Accounts
            .AsNoTracking()
            .Where(account => visible.Contains(account.Id))
            .ToDictionaryAsync(
                account =>
                {
                    return account.Id;
                },
                account =>
                {
                    return account.Name;
                },
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> FindIdsByNameAsync(
        string search,
        CancellationToken cancellationToken)
    {
        // A captured local, so the pattern is a SQL parameter; LikePattern makes it literal as well.
        var pattern = LikePattern.Contains(search);
        var accounts = _dbContext.Accounts
            .AsNoTracking()
            .Where(account => EF.Functions.ILike(account.Name, pattern, LikePattern.EscapeCharacter));

        // Scoped as FindAsync is, and from the caller's own token, never from the request.
        accounts = _currentUser.Caller switch
        {
            AdministratorCaller => accounts,
            CustomerCaller customer => accounts.Where(account => account.Id == customer.AccountId),
            ResellerCaller => accounts.Where(account => false),
            ApiKeyCaller => accounts.Where(account => false),
            AnonymousCaller => accounts.Where(account => false),
            AccountlessCustomerCaller => accounts.Where(account => false),
            _ => throw new UnreachableException("Caller is sealed to SharedKernel: six cases exist."),
        };

        // Not capped and not ordered: the caller filters its own rows by the whole set (see the
        // interface's remarks), so every match must be in it and their order means nothing.
        return await accounts
            .Select(account => account.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// No tenant scope, by contract — see <see cref="IAccountDirectory.FindSystemUserAsync"/>. Unlike
    /// <see cref="FindAsync"/> there is deliberately no <see cref="Sees"/> check: the caller is unattended
    /// work with no principal, and a check against an absent principal would answer nothing in the dark.
    /// The read touches the accounts set only: no plan join, and only the id and system user name leave.
    /// </remarks>
    public async Task<AccountSystemUser?> FindSystemUserAsync(Guid accountId, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // unattended renewal has no principal; the fail-closed filter would answer nothing
        var accounts = _dbContext.Accounts.IgnoreQueryFilters().AsNoTracking();
#pragma warning restore RS0030

        return await accounts
            .Where(account => account.Id == accountId)
            .Select(account => new AccountSystemUser(account.Id, account.Name))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Whether the caller MAY see the account <paramref name="accountId"/>, before the row is read: an
    /// administrator any account, a customer only the one their own token names, and nobody else (a
    /// reseller, whose tenant data touches no account; an anonymous caller; a customer login acting for
    /// no account) none.
    /// </summary>
    /// <param name="accountId">The account asked about.</param>
    /// <returns>True when the caller's tenant scope may include the account.</returns>
    private bool Sees(Guid accountId)
    {
        return _currentUser.Caller switch
        {
            AdministratorCaller => true,
            CustomerCaller customer => customer.AccountId == accountId,
            ResellerCaller => false,
            ApiKeyCaller => false,
            AnonymousCaller => false,
            AccountlessCustomerCaller => false,
            _ => throw new UnreachableException("Caller is sealed to SharedKernel: six cases exist."),
        };
    }

    /// <summary>Reads the snapshot of one account out of a set of accounts, or null when the set holds none.</summary>
    /// <param name="accounts">The accounts the caller's scope admits.</param>
    /// <param name="accountId">The account to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The account's snapshot, or null.</returns>
    private async Task<AccountSnapshot?> ReadSnapshotAsync(
        IQueryable<Account> accounts,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var plans = _dbContext.Plans.AsNoTracking();

        return await ProjectSnapshots(accounts.Where(account => account.Id == accountId), plans)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Joins accounts to their plans and projects each pair to the snapshot other modules read.</summary>
    /// <param name="accounts">The accounts to project.</param>
    /// <param name="plans">The plans the accounts are joined to, under the same scope as the accounts.</param>
    /// <returns>One snapshot per account.</returns>
    private static IQueryable<AccountSnapshot> ProjectSnapshots(
        IQueryable<Account> accounts,
        IQueryable<Plan> plans)
    {
        // Expression lambdas, not the braced form used elsewhere in this repository: EF Core
        // translates these into SQL, and a statement-bodied lambda cannot become an expression tree.
        return accounts.Join(
            plans,
            account => account.PlanId,
            plan => plan.Id,
            (account, plan) => new AccountSnapshot(
                account.Id,
                account.Name,
                plan.MaxSites,
                plan.MaxDatabases,
                plan.MaxSftpUsers,
                plan.MaxCronEntries,
                plan.MaxPhpWorkersPerPool,
                plan.DiskQuotaMb,
                account.Status == AccountStatus.Suspended ? AccountState.Suspended : AccountState.Active,
                plan.MaxFtpUsers,
                account.OwnerEmail,
                account.OwnerLanguage));
    }

    /// <summary>Restricts accounts to those owned by one reseller; the id is a captured parameter, never inlined.</summary>
    /// <param name="accounts">The accounts to restrict.</param>
    /// <param name="resellerId">The owning reseller.</param>
    /// <returns>The accounts whose owner is <paramref name="resellerId"/>.</returns>
    private static IQueryable<Account> WhereOwnedBy(IQueryable<Account> accounts, Guid resellerId)
    {
        return accounts.Where(account => account.ResellerId == resellerId);
    }
}
