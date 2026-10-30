namespace Maran.Modules.Sites.Queries.ListSites;

/// <summary>
/// The public names the sites list sorts by — the <c>sort</c> parameter's vocabulary, written once
/// and read by <see cref="ListSitesSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class SiteSortFields
{
    /// <summary>The site's primary domain.</summary>
    public const string Domain = "domain";

    /// <summary>When the site was created.</summary>
    public const string CreatedAt = "createdAt";

    /// <summary>The site's state.</summary>
    public const string Status = "status";
}
