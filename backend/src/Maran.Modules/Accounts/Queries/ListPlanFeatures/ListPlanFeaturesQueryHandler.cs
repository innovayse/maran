using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Queries.ListPlanFeatures;

/// <summary>
/// Handles <see cref="ListPlanFeaturesQuery"/>: the features the caller may put in a plan, in declaration
/// order, each with its name in the request's culture.
/// </summary>
/// <remarks>
/// <para>
/// Each name is looked up under <c>AccountFeature&lt;Member&gt;</c> in <c>DisplayNames</c>; a member
/// added to <see cref="AccountFeature"/> without its three translations reads as its key, which the
/// handler's test catches by asking for every member.
/// </para>
/// <para>
/// An administrator is offered every feature. A reseller is offered only the features of its own
/// envelope, because a plan selling a feature outside it is refused
/// (<c>ResellerPlanExceedsEnvelope</c>): the form offers what the endpoint accepts, and the SPA holds no
/// list of its own.
/// </para>
/// </remarks>
public sealed class ListPlanFeaturesQueryHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Says whether the caller is a reseller, and which.</summary>
    private readonly ResellerScope _scope;

    /// <summary>Resolves a feature's name in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="scope">Says whether the caller is a reseller, and which.</param>
    /// <param name="displayNames">Resolves a feature's name in the request's culture.</param>
    public ListPlanFeaturesQueryHandler(
        AccountsDbContext dbContext,
        ResellerScope scope,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _scope = scope;
        _displayNames = displayNames;
    }

    /// <summary>Returns every feature the caller may sell in a plan.</summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The features, each with its localized name, or <c>ResellerNotFound</c> for a reseller that no longer exists.</returns>
    public async Task<Result<IReadOnlyList<PlanFeatureDto>>> HandleAsync(
        ListPlanFeaturesQuery query,
        CancellationToken cancellationToken)
    {
        var offered = AccountFeature.All;
        if (_scope.CallerResellerId is { } resellerId)
        {
            var envelope = await _dbContext.Resellers
                .AsNoTracking()
                .Where(reseller => reseller.Id == resellerId)
                .Select(reseller => reseller.Envelope)
                .SingleOrDefaultAsync(cancellationToken);
            if (envelope is null)
            {
                return Result<IReadOnlyList<PlanFeatureDto>>.Fail(
                    Error.Of(nameof(ErrorMessages.ResellerNotFound), ErrorType.NotFound));
            }

            offered = envelope.Features;
        }

        IReadOnlyList<PlanFeatureDto> features = offered.Members()
            .Select(feature =>
            {
                return new PlanFeatureDto(feature, _displayNames[$"AccountFeature{feature}"].Value);
            })
            .ToList();

        return Result<IReadOnlyList<PlanFeatureDto>>.Ok(features);
    }
}
