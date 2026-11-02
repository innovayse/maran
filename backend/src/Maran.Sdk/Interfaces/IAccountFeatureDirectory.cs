using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// A read-only window onto one fact the Accounts module owns and no other module may read from its
/// schema: whether an account's plan includes a feature. Implemented by Accounts; consumed by
/// <see cref="Filters.RequiresAccountFeatureFilter"/> and by anything else that must decide on it.
/// </summary>
/// <remarks>
/// It answers the narrowest value that settles the question — a yes or a no — rather than the plan
/// or its feature set, in the manner of the other directories (rules/architecture.md "A shared
/// facility's contract lives in <c>Maran.Sdk</c>").
/// </remarks>
public interface IAccountFeatureDirectory
{
    /// <summary>Whether the account's plan includes every feature in <paramref name="feature"/>.</summary>
    /// <param name="accountId">The account to ask about.</param>
    /// <param name="feature">The feature, or the set of features, that must all be included.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// <c>true</c> when the account exists, the caller may see it, and its plan includes the feature.
    /// <c>false</c> for a plan that lacks it AND for an account that does not exist or belongs to
    /// somebody else, so the answer discloses nothing about another tenant.
    /// </returns>
    Task<bool> IncludesAsync(Guid accountId, AccountFeature feature, CancellationToken cancellationToken);
}
