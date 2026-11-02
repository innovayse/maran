using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Identity.Queries.ListGrantableFeatures;

/// <summary>
/// Handles <see cref="ListGrantableFeaturesQuery"/>: every feature the panel can gate, in declaration
/// order, with its name in the caller's language.
/// </summary>
/// <remarks>
/// Only an account's owner asks (the same guard as the list), because the answer exists to fill the
/// form only an owner can use. The set is every member of <see cref="AccountFeature"/>: the account's
/// plan still decides which of them work, so a feature the plan lacks is harmless to tick and refused
/// on use — narrowing the choices to the plan would need a read of the Accounts module this window
/// does not have.
/// </remarks>
public sealed class ListGrantableFeaturesQueryHandler
{
    /// <summary>Key prefix of a feature's name in <see cref="DisplayNames"/>.</summary>
    private const string KeyPrefix = "AccountFeature";

    /// <summary>Establishes that the caller owns the account they are acting for.</summary>
    private readonly SubUserOwnerGuard _guard;

    /// <summary>Resolves a feature's name for the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="guard">Establishes that the caller owns the account they are acting for.</param>
    /// <param name="displayNames">Resolves a feature's name for the request's culture.</param>
    public ListGrantableFeaturesQueryHandler(SubUserOwnerGuard guard, IStringLocalizer<DisplayNames> displayNames)
    {
        _guard = guard;
        _displayNames = displayNames;
    }

    /// <summary>Lists the features.</summary>
    /// <param name="query">The (empty) query.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Every grantable feature with its label, or a typed refusal.</returns>
    public async Task<Result<IReadOnlyList<GrantableFeatureDto>>> HandleAsync(
        ListGrantableFeaturesQuery query,
        CancellationToken cancellationToken)
    {
        var owned = await _guard.RequireOwnedAccountAsync(cancellationToken);
        if (!owned.IsSuccess)
        {
            return Result<IReadOnlyList<GrantableFeatureDto>>.Fail(owned.Error!);
        }

        IReadOnlyList<GrantableFeatureDto> features = AccountFeature.All.Members()
            .Select(feature =>
            {
                return new GrantableFeatureDto(feature, _displayNames[KeyPrefix + feature].Value);
            })
            .ToList();

        return Result<IReadOnlyList<GrantableFeatureDto>>.Ok(features);
    }
}
