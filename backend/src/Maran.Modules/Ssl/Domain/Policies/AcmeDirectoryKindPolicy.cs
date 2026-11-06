namespace Maran.Modules.Ssl.Domain.Policies;

/// <summary>
/// Names which kind of certificate authority a configured ACME directory URL points at. The answer
/// comes from an exact match against the two well-known Let's Encrypt directory documents and from
/// nothing else: a URL that merely CONTAINS the word "staging", or that points at the same host with
/// another path, is a custom authority, because a guess made from the spelling of an address is how an
/// operator is told a real authority is a sandbox (or the reverse).
/// </summary>
public static class AcmeDirectoryKindPolicy
{
    /// <summary>The directory document of Let's Encrypt's production authority.</summary>
    public const string ProductionUrl = "https://acme-v02.api.letsencrypt.org/directory";

    /// <summary>The directory document of Let's Encrypt's staging authority, whose certificates no browser trusts.</summary>
    public const string StagingUrl = "https://acme-staging-v02.api.letsencrypt.org/directory";

    /// <summary>The kind of the production authority.</summary>
    public const string Production = "production";

    /// <summary>The kind of the staging authority.</summary>
    public const string Staging = "staging";

    /// <summary>The kind of every other authority, a private one included.</summary>
    public const string Custom = "custom";

    /// <summary>Classifies a directory URL.</summary>
    /// <param name="directoryUrl">The configured directory URL.</param>
    /// <returns><see cref="Production"/> or <see cref="Staging"/> on an exact match, otherwise <see cref="Custom"/>.</returns>
    public static string KindOf(string directoryUrl)
    {
        if (string.Equals(directoryUrl, ProductionUrl, StringComparison.Ordinal))
        {
            return Production;
        }

        if (string.Equals(directoryUrl, StagingUrl, StringComparison.Ordinal))
        {
            return Staging;
        }

        return Custom;
    }
}
