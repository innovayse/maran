namespace Maran.Modules.Sites.Domain.Enums;

/// <summary>How a redirect rule's path is compared with the request path.</summary>
public enum RedirectMatch
{
    /// <summary>The request path equals the rule's path.</summary>
    Exact = 1,

    /// <summary>The request path starts with the rule's path, which ends in "/".</summary>
    Prefix = 2,
}
