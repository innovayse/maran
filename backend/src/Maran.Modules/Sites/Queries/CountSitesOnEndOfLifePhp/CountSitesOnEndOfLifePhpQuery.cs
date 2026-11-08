namespace Maran.Modules.Sites.Queries.CountSitesOnEndOfLifePhp;

/// <summary>
/// Counts the sites the caller may see that run a PHP version whose security support has ended, for a
/// screen that shows how many need attention rather than which. A count is asked as a count: the
/// sites list is paged, so a page of it holds one page and not the total.
/// </summary>
/// <remarks>
/// Dispatched by the panel itself, never bound from a request, so it carries no validator. The day
/// the version is judged against is the panel's clock at the moment of the read, taken by the
/// handler, so a caller cannot ask about another day.
/// </remarks>
public sealed record CountSitesOnEndOfLifePhpQuery;
