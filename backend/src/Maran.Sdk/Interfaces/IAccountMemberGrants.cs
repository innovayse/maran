using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// A read-only window onto one fact the Identity module owns and no other module may read from its
/// schema: whether a signed-in person may use a feature of an account, given how they stand towards
/// it. Implemented by Identity; consumed by <see cref="Filters.RequiresAccountFeatureFilter"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>It answers a different question from <see cref="IAccountFeatureDirectory"/>, and the two are
/// asked together.</b> The directory says whether the account's PLAN sells the feature; this says
/// whether THIS PERSON has been given it. A feature is usable only when both say yes, so a plan that
/// drops a feature stops everyone, and an owner who withholds one from a team member stops that member
/// whatever the plan includes.
/// </para>
/// <para>
/// It answers the narrowest value that settles the question — a yes or a no — and fails closed: a
/// person who holds no standing towards the account at all (a membership removed while their access
/// token was still live) is told no, so removing a person's access does not wait for their token to
/// expire on the endpoints that ask.
/// </para>
/// </remarks>
public interface IAccountMemberGrants
{
    /// <summary>Whether the person may use <paramref name="feature"/> of the account.</summary>
    /// <param name="userId">The signed-in person, from their own access token.</param>
    /// <param name="accountId">The account their token currently speaks for.</param>
    /// <param name="feature">The single feature asked about.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// <c>true</c> for the account's owner, and for a member the owner granted the feature to;
    /// <c>false</c> for a member it was not granted to, and for a person with no standing towards the
    /// account at all.
    /// </returns>
    Task<bool> PermitsAsync(Guid userId, Guid accountId, AccountFeature feature, CancellationToken cancellationToken);
}
