namespace Maran.Sdk.Contracts;

/// <summary>
/// A kind of thing the global search finds. Each value is owned by exactly one module, which answers
/// for it through <see cref="Interfaces.ISearchProvider"/>, and by exactly one screen in the SPA, which
/// knows which route a hit of that kind opens.
/// </summary>
/// <remarks>
/// A closed set on purpose: the server never sends a URL or a route name, only the kind and the id, so
/// the SPA stays the only place that knows its own paths. A module that gains a searchable kind adds a
/// value here, in the same change that adds its provider and the SPA's mapping for it.
/// </remarks>
public enum SearchKind
{
    /// <summary>A hosting account, found by name, primary domain or owner e-mail. Owned by Accounts.</summary>
    Account,

    /// <summary>A site, found by any hostname it claims. Owned by Sites.</summary>
    Site,
}
