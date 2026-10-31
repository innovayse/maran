namespace Maran.Modules.Sites.Domain.Policies;

/// <summary>
/// How many aliases one site may carry, derived from how many names one certificate may cover —
/// stated once for the entity, the validator and the limits the site form is shown.
/// </summary>
/// <remarks>
/// <para>
/// Every name a site answers for is rendered into its vhost's <c>server_name</c>, claimed as a row
/// and, once the site is secured, listed in its certificate's subject alternative names. Without a
/// bound all three grow with whatever a request carries.
/// </para>
/// <para>
/// The bound is the certificate's, because a site whose names one certificate cannot cover is a
/// site the panel cannot secure: Let's Encrypt refuses an order naming more than 100 identifiers
/// ("Names per Certificate", https://letsencrypt.org/docs/rate-limits/). The primary domain is one
/// of them, so the aliases get the rest. The agent holds the same bound on its own side for the
/// rpcs that render serving configuration (rules/architecture.md "Agent").
/// </para>
/// </remarks>
public static class SiteAliasLimit
{
    /// <summary>The most names one certificate may cover, the primary domain included.</summary>
    public const int CertificateNameLimit = 100;

    /// <summary>The most aliases one site may carry: every certificate name but the domain's own.</summary>
    public const int MaximumAliases = CertificateNameLimit - 1;
}
