using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.SharedKernel.Callers;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Queries.GetMyReseller;

/// <summary>
/// Handles <see cref="GetMyResellerQuery"/> by reading the one reseller the caller's own token names,
/// with its envelope and what it owns.
/// </summary>
/// <remarks>
/// The reseller id comes from the caller's token and from nowhere else. Anybody who is not a reseller,
/// an administrator included, owns none and gets the same <c>ResellerNotFound</c> as a reseller whose
/// row is gone, so the answer says nothing about which.
/// </remarks>
public sealed class GetMyResellerQueryHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The authenticated principal, whose own token names the reseller.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Measures what the reseller owns.</summary>
    private readonly ResellerUsageReader _usage;

    /// <summary>Names the reseller's state in the response, in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="usage">Measures what the reseller owns.</param>
    /// <param name="displayNames">Names the reseller's state in the response, in the request's culture.</param>
    public GetMyResellerQueryHandler(
        AccountsDbContext dbContext,
        ICurrentUser currentUser,
        ResellerUsageReader usage,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _usage = usage;
        _displayNames = displayNames;
    }

    /// <summary>Returns the caller's own reseller, or a typed failure when the caller is none.</summary>
    /// <param name="query">The empty request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The reseller as a <see cref="ResellerDetailDto"/>, or <c>ResellerNotFound</c>.</returns>
    public async Task<Result<ResellerDetailDto>> HandleAsync(
        GetMyResellerQuery query,
        CancellationToken cancellationToken)
    {
        if (_currentUser.Caller is not ResellerCaller caller)
        {
            return Result<ResellerDetailDto>.Fail(Error.Of(nameof(ErrorMessages.ResellerNotFound), ErrorType.NotFound));
        }

        var reseller = await _dbContext.Resellers
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == caller.ResellerId, cancellationToken);
        if (reseller is null)
        {
            return Result<ResellerDetailDto>.Fail(Error.Of(nameof(ErrorMessages.ResellerNotFound), ErrorType.NotFound));
        }

        var usage = await _usage.ReadAsync(reseller.Id, cancellationToken);
        return Result<ResellerDetailDto>.Ok(ResellerMapper.ToDetailDto(reseller, usage, _displayNames));
    }
}
