using Maran.Modules.Sites.Domain.ValueObjects;

namespace Maran.Modules.Sites.Domain.Policies;

/// <summary>
/// Names the groups of a site's options and says which of them a replacement changed, so the audit
/// entry can state what changed without ever holding a value.
/// </summary>
public static class SiteOptionGroups
{
    /// <summary>The canonical-host choice.</summary>
    public const string CanonicalHost = "canonical-host";

    /// <summary>The request body size limit.</summary>
    public const string UploadSize = "upload-size";

    /// <summary>The static-file cache lifetime.</summary>
    public const string Cache = "cache";

    /// <summary>The redirect rules.</summary>
    public const string Redirects = "redirects";

    /// <summary>The basic-authentication users and their passwords.</summary>
    public const string BasicAuth = "basic-auth";

    /// <summary>The IP allow / deny list.</summary>
    public const string IpRules = "ip-rules";

    /// <summary>The request limit.</summary>
    public const string RequestRate = "request-rate";

    /// <summary>Lists the groups whose content differs between two sets of options.</summary>
    /// <param name="before">The options as they were stored.</param>
    /// <param name="after">The options as the replacement states them.</param>
    /// <returns>The group names that changed, in a fixed order; empty when nothing did.</returns>
    public static IReadOnlyList<string> Changed(SiteOptionsValues before, SiteOptionsValues after)
    {
        var changed = new List<string>();
        if (before.CanonicalHost != after.CanonicalHost)
        {
            changed.Add(CanonicalHost);
        }

        if (before.ClientMaxBodySizeMib != after.ClientMaxBodySizeMib)
        {
            changed.Add(UploadSize);
        }

        if (before.StaticCacheDays != after.StaticCacheDays)
        {
            changed.Add(Cache);
        }

        if (!before.Redirects.SequenceEqual(after.Redirects))
        {
            changed.Add(Redirects);
        }

        if (!before.BasicAuthUsers.SequenceEqual(after.BasicAuthUsers))
        {
            changed.Add(BasicAuth);
        }

        if (before.IpAccessMode != after.IpAccessMode || !before.IpRules.SequenceEqual(after.IpRules))
        {
            changed.Add(IpRules);
        }

        if (before.RequestRate != after.RequestRate)
        {
            changed.Add(RequestRate);
        }

        return changed;
    }
}
