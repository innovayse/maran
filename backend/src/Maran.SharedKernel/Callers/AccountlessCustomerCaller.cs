namespace Maran.SharedKernel.Callers;

/// <summary>
/// A signed-in customer login that currently acts for no hosting account: it belongs to none, or its
/// token names none.
/// </summary>
/// <remarks>
/// This is a real state, not a stand-in for a missing value: a customer whose memberships are all gone
/// still signs in, and still has to reach the endpoints that need no account (their own profile,
/// signing out). It owns nothing, so a tenant query filter shows it no row; it is also not
/// <see cref="AnonymousCaller"/>, because it has an identity the audit journal must record. It has no
/// account id for the same reason an administrator has none — there is no such member to read.
/// </remarks>
public sealed record AccountlessCustomerCaller : PrincipalCaller
{
    /// <summary>Creates a customer caller that acts for no account.</summary>
    /// <param name="userId">The customer's panel user id.</param>
    /// <param name="username">The customer's login name.</param>
    /// <param name="viewedBy">The operator viewing as this customer, or null.</param>
    public AccountlessCustomerCaller(Guid userId, string username, ViewingOperator? viewedBy = null)
        : base(userId, username, viewedBy)
    {
    }
}
