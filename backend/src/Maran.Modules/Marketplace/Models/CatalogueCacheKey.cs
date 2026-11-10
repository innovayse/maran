namespace Maran.Modules.Marketplace.Models;

/// <summary>
/// What one cached catalogue page is for. Channel and locale are the key the contract names; the search
/// term and the cursor extend it because two requests that differ in either are different pages, and a
/// page served for the wrong search would be a wrong answer.
/// </summary>
/// <param name="Channel">The release channel.</param>
/// <param name="Locale">The interface language.</param>
/// <param name="Search">The search term, empty for none.</param>
/// <param name="Cursor">The page cursor, empty for the first page.</param>
public sealed record CatalogueCacheKey(string Channel, string Locale, string Search, string Cursor);
