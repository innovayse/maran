using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Queries.ListPlanFeatures;

/// <summary>
/// Handles <see cref="ListPlanFeaturesQuery"/>: the panel's features in declaration order, each
/// with its name in the request's culture.
/// </summary>
/// <remarks>
/// Each name is looked up under <c>AccountFeature&lt;Member&gt;</c> in <c>DisplayNames</c>; a member
/// added to <see cref="AccountFeature"/> without its three translations reads as its key, which the
/// handler's test catches by asking for every member.
/// </remarks>
public sealed class ListPlanFeaturesQueryHandler
{
    /// <summary>Resolves a feature's name in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="displayNames">Resolves a feature's name in the request's culture.</param>
    public ListPlanFeaturesQueryHandler(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Returns every feature a plan can sell.</summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <returns>The features, each with its localized name.</returns>
    public Task<Result<IReadOnlyList<PlanFeatureDto>>> HandleAsync(ListPlanFeaturesQuery query)
    {
        IReadOnlyList<PlanFeatureDto> features = AccountFeature.All.Members()
            .Select(feature =>
            {
                return new PlanFeatureDto(feature, _displayNames[$"AccountFeature{feature}"].Value);
            })
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<PlanFeatureDto>>.Ok(features));
    }
}
