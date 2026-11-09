using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Accounts.Queries.GetMyBranding;

/// <summary>
/// Handles <see cref="GetMyBrandingQuery"/> by reading the branding of the reseller that owns the
/// account the caller acts for.
/// </summary>
/// <remarks>
/// <para>
/// Every caller gets an answer, and "the panel's own" is the answer for anybody the question does not
/// apply to: an administrator, a reseller, an account the administrator owns, a reseller that set
/// nothing. The shell asks on every sign-in of a customer, and a 404 for the ordinary case would be a
/// failed request in the browser's console on most panels.
/// </para>
/// <para>
/// The account comes from the caller's token. The branding is read through the tenant filter, which
/// shows a customer the branding of its own account's reseller and no other; the account is named in the
/// predicate as well, so the answer does not depend on the filter alone being right.
/// </para>
/// </remarks>
public sealed class GetMyBrandingQueryHandler
{
    /// <summary>The authenticated principal, whose own token names the account.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="dbContext">The Accounts module's database context.</param>
    public GetMyBrandingQueryHandler(ICurrentUser currentUser, AccountsDbContext dbContext)
    {
        _currentUser = currentUser;
        _dbContext = dbContext;
    }

    /// <summary>Returns the branding the caller's shell shows.</summary>
    /// <param name="query">The empty request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The branding, with every member empty when the panel's own applies.</returns>
    public async Task<Result<ResellerBrandingDto>> HandleAsync(
        GetMyBrandingQuery query,
        CancellationToken cancellationToken)
    {
        if (_currentUser.Caller is not CustomerCaller { AccountId: var accountId })
        {
            return Result<ResellerBrandingDto>.Ok(ResellerBrandingMapper.ToDto(null));
        }

        var branding = await _dbContext.ResellerBrandings
            .AsNoTracking()
            .Where(b => _dbContext.Accounts.Any(a => a.Id == accountId && a.ResellerId == b.ResellerId))
            .SingleOrDefaultAsync(cancellationToken);

        return Result<ResellerBrandingDto>.Ok(ResellerBrandingMapper.ToDto(branding));
    }
}
