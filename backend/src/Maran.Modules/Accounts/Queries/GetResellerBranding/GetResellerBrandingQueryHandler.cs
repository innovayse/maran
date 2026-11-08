using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;

namespace Maran.Modules.Accounts.Queries.GetResellerBranding;

/// <summary>
/// Handles <see cref="GetResellerBrandingQuery"/> by reading the branding of the reseller the caller's
/// own token names, for the reseller's settings screen.
/// </summary>
/// <remarks>
/// A reseller that never set any is answered with every member empty, not with a 404: "nothing set" is a
/// state of the settings form. Anybody who is not a reseller gets <c>ResellerNotFound</c>.
/// </remarks>
public sealed class GetResellerBrandingQueryHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>States which reseller the caller is.</summary>
    private readonly ResellerScope _scope;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="scope">States which reseller the caller is.</param>
    public GetResellerBrandingQueryHandler(AccountsDbContext dbContext, ResellerScope scope)
    {
        _dbContext = dbContext;
        _scope = scope;
    }

    /// <summary>Returns the caller's own reseller's branding.</summary>
    /// <param name="query">The empty request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The branding, with every member empty when none is set, or <c>ResellerNotFound</c>.</returns>
    public async Task<Result<ResellerBrandingDto>> HandleAsync(
        GetResellerBrandingQuery query,
        CancellationToken cancellationToken)
    {
        if (_scope.CallerResellerId is not { } resellerId)
        {
            return Result<ResellerBrandingDto>.Fail(Error.Of(nameof(ErrorMessages.ResellerNotFound), ErrorType.NotFound));
        }

        var branding = await _dbContext.ResellerBrandings
            .AsNoTracking()
            .SingleOrDefaultAsync(b => b.ResellerId == resellerId, cancellationToken);

        return Result<ResellerBrandingDto>.Ok(ResellerBrandingMapper.ToDto(branding));
    }
}
