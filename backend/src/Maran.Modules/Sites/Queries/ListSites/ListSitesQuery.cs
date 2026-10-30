namespace Maran.Modules.Sites.Queries.ListSites;

/// <summary>
/// Lists the sites the caller may see, optionally narrowed to one account.
/// </summary>
/// <remarks>
/// The tenant scope never comes from this query: the context's global query filter scopes the read to
/// the caller's own account (rules/security.md item 6). <paramref name="AccountId"/> only narrows that
/// scope further, and the two are combined with AND — a customer naming another account's id gets an
/// empty list, never that account's rows.
/// </remarks>
/// <param name="AccountId">
/// The account whose sites to list, or <c>null</c> for every account the caller may see. Bound from the
/// <c>accountId</c> query-string parameter.
/// </param>
public sealed record ListSitesQuery(Guid? AccountId = null);
