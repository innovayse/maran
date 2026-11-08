using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Modules.Sites.Domain.Entities;

/// <summary>One redirect rule of a site's options. Created and removed with <see cref="SiteOptions"/> and by nothing else.</summary>
public sealed class SiteRedirect
{
    /// <summary>The rule's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The site whose options hold this rule.</summary>
    public Guid SiteId { get; private set; }

    /// <summary>The options this rule belongs to, as the relationship EF Core cascades on and the tenant filter reads through.</summary>
    public SiteOptions Options { get; private set; }

    /// <summary>The rule's place in the rendered order, from zero.</summary>
    public int Position { get; private set; }

    /// <summary>The path the rule applies to.</summary>
    public string Path { get; private set; }

    /// <summary>How <see cref="Path"/> is compared with the request path.</summary>
    public RedirectMatch Match { get; private set; }

    /// <summary>Where requests are sent: an absolute http(s) URL or a path on the same site.</summary>
    public string Target { get; private set; }

    /// <summary>The HTTP status the redirect answers with.</summary>
    public RedirectStatus Status { get; private set; }

    /// <summary>Creates a rule for <paramref name="options"/>.</summary>
    /// <param name="options">The options that hold the rule.</param>
    /// <param name="position">The rule's place in the rendered order.</param>
    /// <param name="path">The path the rule applies to.</param>
    /// <param name="match">How the path is compared.</param>
    /// <param name="target">Where requests are sent.</param>
    /// <param name="status">The HTTP status of the redirect.</param>
    public SiteRedirect(
        SiteOptions options,
        int position,
        string path,
        RedirectMatch match,
        string target,
        RedirectStatus status)
    {
        Id = Guid.NewGuid();
        Options = options;
        SiteId = options.SiteId;
        Position = position;
        Path = path;
        Match = match;
        Target = target;
        Status = status;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private SiteRedirect()
    {
        Options = null!;
        Path = string.Empty;
        Target = string.Empty;
    }
}
