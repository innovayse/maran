namespace Maran.Modules.Sites.Domain.Policies;

/// <summary>
/// The bounds and shapes of a site's options, stated once for the entity, the validator and the limits
/// the options screen is shown.
/// </summary>
/// <remarks>
/// Every count is a cap because every option is rendered into a root-written web-server configuration:
/// without one the vhost grows with whatever a request carries. The agent holds the same bounds on its
/// own side (rules/architecture.md "Agent"); a number changed here without the agent's is a request the
/// panel accepts and the agent refuses.
/// </remarks>
public static class SiteOptionsLimits
{
    /// <summary>The most redirect rules one site may carry.</summary>
    public const int MaximumRedirects = 25;

    /// <summary>The most basic-authentication users one site may carry.</summary>
    public const int MaximumBasicAuthUsers = 10;

    /// <summary>The most IP rules (addresses or ranges) one site may carry.</summary>
    public const int MaximumIpRules = 25;

    /// <summary>The smallest request body limit that may be set, in MiB.</summary>
    public const int MinimumBodySizeMib = 1;

    /// <summary>The largest request body limit that may be set, in MiB.</summary>
    public const int MaximumBodySizeMib = 1024;

    /// <summary>The smallest static-file cache lifetime that may be set, in days.</summary>
    public const int MinimumCacheDays = 1;

    /// <summary>The largest static-file cache lifetime that may be set, in days.</summary>
    public const int MaximumCacheDays = 365;

    /// <summary>The smallest request rate that may be set, in requests per second per visitor address.</summary>
    public const int MinimumRequestsPerSecond = 1;

    /// <summary>The largest request rate that may be set, in requests per second per visitor address.</summary>
    public const int MaximumRequestsPerSecond = 1000;

    /// <summary>The smallest request burst that may be set; zero refuses every request above the rate.</summary>
    public const int MinimumRequestBurst = 0;

    /// <summary>The largest request burst that may be set.</summary>
    public const int MaximumRequestBurst = 1000;

    /// <summary>The longest redirect path, in characters.</summary>
    public const int MaximumRedirectPathLength = 255;

    /// <summary>The longest redirect target, in characters.</summary>
    public const int MaximumRedirectTargetLength = 1024;

    /// <summary>The longest basic-authentication login name, in characters.</summary>
    public const int MaximumUserNameLength = 32;

    /// <summary>The shortest basic-authentication password, in characters.</summary>
    public const int MinimumPasswordLength = 8;

    /// <summary>
    /// The longest basic-authentication password, in UTF-8 bytes. bcrypt reads 72 bytes and ignores the
    /// rest, so a longer password would be accepted and then only partly checked; it is refused instead.
    /// </summary>
    public const int MaximumPasswordBytes = 72;

    /// <summary>A basic-authentication login name: ASCII letters, digits, dot, underscore and hyphen.</summary>
    /// <remarks>The colon that separates a name from its hash in the credential file cannot occur in it.</remarks>
    public const string UserNamePattern = @"\A[A-Za-z0-9._-]{1,32}\z";
}
