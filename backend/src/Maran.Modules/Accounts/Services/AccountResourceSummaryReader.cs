using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// Builds an account's resource summary: asks every composed module's
/// <see cref="IAccountResourceCounter"/> for its count and pairs each with the plan's limit through
/// <see cref="Domain.Entities.Plan.LimitFor"/>.
/// </summary>
/// <remarks>
/// <para>
/// Shared by the administrator's query and the customer's own: the two differ only in where the
/// account id comes from (the route, or the caller's token), and a second copy of the pairing would
/// be a second place the summary could drift.
/// </para>
/// <para>
/// The counters are asked one after another, not in parallel. They are cheap — five row counts and
/// one agent listing — and the Cron counter resolves the account through this module's own
/// request-scoped context, which a concurrent read on the same context would break.
/// </para>
/// <para>
/// Every count runs in the caller's tenant scope, so a customer's summary can only ever count the
/// customer's own rows; nothing here bypasses a query filter.
/// </para>
/// <para>
/// <b>A reseller is shown the limits and not the usage.</b> A reseller's request runs as a reseller in
/// every module, and in every module that owns a resource a reseller sees no row — it reaches a
/// customer's sites, databases and logins only through a view-as session. Asking those counters would
/// therefore answer zero for an account that owns plenty, and <c>zero is a real count</c> on the wire.
/// So for a reseller each kind is stated as unavailable, which is the statement that carries no number.
/// The account itself is still resolved through this module's own filtered context first, so another
/// reseller's account is <c>AccountNotFound</c> before anything is asked.
/// </para>
/// </remarks>
public sealed class AccountResourceSummaryReader
{
    /// <summary>The Accounts module's database context, carrying the caller's tenant scope.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Says whether the caller is a reseller, which no counter can answer for.</summary>
    private readonly ResellerScope _scope;

    /// <summary>One counter per composed module that owns a resource kind.</summary>
    private readonly IEnumerable<IAccountResourceCounter> _counters;

    /// <summary>Creates the reader.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="scope">Says whether the caller is a reseller, which no counter can answer for.</param>
    /// <param name="counters">Every registered counter; empty when no counting module is composed.</param>
    public AccountResourceSummaryReader(
        AccountsDbContext dbContext,
        ResellerScope scope,
        IEnumerable<IAccountResourceCounter> counters)
    {
        _dbContext = dbContext;
        _scope = scope;
        _counters = counters;
    }

    /// <summary>Reads the summary of one account.</summary>
    /// <param name="accountId">The account to summarise.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The summary, or <c>AccountNotFound</c> when the caller's scope holds no such account.</returns>
    public async Task<Result<AccountResourceSummaryDto>> ReadAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var plan = await _dbContext.Accounts
            .AsNoTracking()
            .Where(account => account.Id == accountId)
            .Join(
                _dbContext.Plans.AsNoTracking(),
                account => account.PlanId,
                candidate => candidate.Id,
                (account, candidate) => candidate)
            .SingleOrDefaultAsync(cancellationToken);

        if (plan is null)
        {
            return Result<AccountResourceSummaryDto>.Fail(
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        var countable = _scope.CallerResellerId is null;
        var rows = new List<AccountResourceUsageDto>();
        foreach (var counter in _counters.OrderBy(registered =>
        {
            return registered.Kind;
        }))
        {
            var count = countable
                ? await counter.CountAsync(accountId, cancellationToken)
                : AccountResourceCount.Unavailable(counter.Kind);
            var limit = plan.LimitFor(count.Kind);
            rows.Add(AccountResourceUsageMapper.ToDto(count, limit));
        }

        return Result<AccountResourceSummaryDto>.Ok(new AccountResourceSummaryDto(accountId, rows));
    }
}
