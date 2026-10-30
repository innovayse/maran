using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
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
/// </remarks>
public sealed class AccountResourceSummaryReader
{
    /// <summary>The Accounts module's database context, carrying the caller's tenant scope.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>One counter per composed module that owns a resource kind.</summary>
    private readonly IEnumerable<IAccountResourceCounter> _counters;

    /// <summary>Creates the reader.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="counters">Every registered counter; empty when no counting module is composed.</param>
    public AccountResourceSummaryReader(AccountsDbContext dbContext, IEnumerable<IAccountResourceCounter> counters)
    {
        _dbContext = dbContext;
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

        var rows = new List<AccountResourceUsageDto>();
        foreach (var counter in _counters.OrderBy(registered =>
        {
            return registered.Kind;
        }))
        {
            var count = await counter.CountAsync(accountId, cancellationToken);
            var limit = plan.LimitFor(count.Kind);
            rows.Add(new AccountResourceUsageDto(count.Kind, count.State, count.Used, limit.Kind, limit.Maximum));
        }

        return Result<AccountResourceSummaryDto>.Ok(new AccountResourceSummaryDto(accountId, rows));
    }
}
