namespace Maran.Agent.Client.Services.SitesService;

/// <summary>How a redirect rule's path is compared with the request path, as the panel names it.</summary>
/// <remarks>
/// A panel-side mirror of the wire's <c>RedirectMatch</c>. It has no "unspecified" member: the agent
/// refuses that value, so a caller must not be able to express it.
/// </remarks>
public enum RedirectMatchKind
{
    /// <summary>The request path equals the rule's path.</summary>
    Exact = 1,

    /// <summary>The request path starts with the rule's path, which ends in "/".</summary>
    Prefix = 2,
}
