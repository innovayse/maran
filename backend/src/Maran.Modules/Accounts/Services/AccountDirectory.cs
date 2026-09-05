using Maran.Modules.Accounts.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

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
/// <see cref="ListAsync"/> is the deliberate exception and the interface says so: it answers with
/// every account on the host and applies no scope at all, because the host-wide disk view it exists
/// for has no tenant to scope by. Its authorization lives entirely at its caller's boundary.
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
        // An administrator sees every account; a customer sees exactly the one they own. The
        // comparison is against the id the caller's own token carries, never against anything on
        // the request, so a forged or guessed account id cannot widen it.
        if (!_currentUser.IsAdmin && _currentUser.AccountId != accountId)
        {
            return null;
        }

        // Expression lambdas, not the braced form used elsewhere in this repository: EF Core
        // translates these into SQL, and a statement-bodied lambda cannot become an expression tree.
        return await _dbContext.Accounts
            .AsNoTracking()
            .Where(account => account.Id == accountId)
            .Join(
                _dbContext.Plans.AsNoTracking(),
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
                    plan.DiskQuotaMb))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// No tenant scope, by contract — see <see cref="IAccountDirectory.ListAsync"/>, which states
    /// the exposure and puts the whole authorization burden on the caller. There is deliberately no
    /// <c>IsAdmin</c> check here to mirror <see cref="FindAsync"/>'s: this method runs from the
    /// administrator's host disk view and would also have to run from a timer one day, where there
    /// is no principal at all and a check against one would silently answer "nothing" rather than
    /// fail — the failure mode <c>CertificateRenewalHandler</c> and <c>TaskRetentionHandler</c> both
    /// record. A check that answers emptiness instead of refusing is not a security control, it is
    /// a feature that stops working in the dark.
    /// </para>
    /// <para>
    /// There is no <c>IgnoreQueryFilters()</c> call either, and its absence is deliberate rather
    /// than an oversight: <see cref="AccountsDbContext"/> declares no global query filter on
    /// <c>Account</c> — this module's tenant boundary is the explicit comparison in
    /// <see cref="FindAsync"/>, not a filter — so the call would suppress nothing. A defensive call
    /// that cannot fail is deleted rather than labelled (rules/testing.md); it would age into a
    /// reader's belief that a filter exists here.
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
        // Expression lambdas for the same reason FindAsync uses them: EF Core translates these into
        // SQL, and a statement-bodied lambda cannot become an expression tree.
        return await _dbContext.Accounts
            .AsNoTracking()
            .Join(
                _dbContext.Plans.AsNoTracking(),
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
                    plan.DiskQuotaMb))
            .ToListAsync(cancellationToken);
    }
}
