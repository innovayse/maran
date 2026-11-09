namespace Maran.Sdk.Contracts;

/// <summary>How a redirect rule's path is compared with the request path, as one module reports it to another.</summary>
/// <remarks>A deliberate second declaration of a closed set the Sites module owns, for the reason <see cref="SiteBackend"/> gives.</remarks>
public enum SiteRedirectMatch
{
    /// <summary>The request path equals the rule's path.</summary>
    Exact = 1,

    /// <summary>The request path starts with the rule's path, which ends in "/".</summary>
    Prefix = 2,
}
