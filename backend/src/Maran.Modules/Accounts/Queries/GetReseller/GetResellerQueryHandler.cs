using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Queries.GetReseller;

/// <summary>Handles <see cref="GetResellerQuery"/> by reading one row of <c>accounts.Resellers</c> and measuring what it owns.</summary>
public sealed class GetResellerQueryHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Measures what the reseller owns.</summary>
    private readonly ResellerUsageReader _usage;

    /// <summary>Names the reseller's state in the response, in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="usage">Measures what the reseller owns.</param>
    /// <param name="displayNames">Names the reseller's state in the response, in the request's culture.</param>
    public GetResellerQueryHandler(
        AccountsDbContext dbContext,
        ResellerUsageReader usage,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _usage = usage;
        _displayNames = displayNames;
    }

    /// <summary>Returns the reseller, or a typed failure when there is none.</summary>
    /// <param name="query">Which reseller to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The reseller as a <see cref="ResellerDetailDto"/>, or <c>ResellerNotFound</c>.</returns>
    public async Task<Result<ResellerDetailDto>> HandleAsync(GetResellerQuery query, CancellationToken cancellationToken)
    {
        var reseller = await _dbContext.Resellers
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == query.ResellerId, cancellationToken);
        if (reseller is null)
        {
            return Result<ResellerDetailDto>.Fail(Error.Of(nameof(ErrorMessages.ResellerNotFound), ErrorType.NotFound));
        }

        var usage = await _usage.ReadAsync(reseller.Id, cancellationToken);
        return Result<ResellerDetailDto>.Ok(ResellerMapper.ToDetailDto(reseller, usage, _displayNames));
    }
}
