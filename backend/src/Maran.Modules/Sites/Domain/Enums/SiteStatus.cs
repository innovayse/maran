namespace Maran.Modules.Sites.Domain.Enums;

/// <summary>Whether a site serves its own content.</summary>
/// <remarks>
/// A disabled site keeps its vhost, its aliases and its log paths — the agent serves a suspension
/// response in place of the content rather than removing the site — so this is a serving state, not
/// a lifecycle state. A site that no longer exists has no row at all.
/// </remarks>
public enum SiteStatus
{
    /// <summary>The site serves its own content.</summary>
    Enabled = 1,

    /// <summary>The site's vhost is in place but answers with a suspension response.</summary>
    Disabled = 2,
}
