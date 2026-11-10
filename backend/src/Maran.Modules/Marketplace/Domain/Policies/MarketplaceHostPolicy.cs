using Maran.Modules.Marketplace.Options;

namespace Maran.Modules.Marketplace.Domain.Policies;

/// <summary>
/// The one rule for "is this address the marketplace host". Every redirect target, every download
/// address and every request path is judged here against the configured address, so the host allow-list
/// has a single definition and no code path compares host names in its own way.
/// </summary>
public static class MarketplaceHostPolicy
{
    /// <summary>The configured address as a <see cref="Uri"/>.</summary>
    /// <param name="options">Options that passed <c>MarketplaceOptionsValidator</c>, so the address is an https URL with a host.</param>
    /// <returns>The address, which every later comparison is made against.</returns>
    public static Uri BaseUri(MarketplaceOptions options)
    {
        return new Uri(options.BaseUrl, UriKind.Absolute);
    }

    /// <summary>
    /// Whether an address is on the configured host: https, the same host name (case-insensitively), the
    /// same port, and no user information.
    /// </summary>
    /// <param name="candidate">The address to judge.</param>
    /// <param name="baseUri">The configured address from <see cref="BaseUri"/>.</param>
    /// <returns>True only for an address the panel may contact.</returns>
    public static bool IsConfiguredHost(Uri candidate, Uri baseUri)
    {
        return candidate.IsAbsoluteUri
            && candidate.Scheme == Uri.UriSchemeHttps
            && candidate.UserInfo.Length == 0
            && string.Equals(candidate.IdnHost, baseUri.IdnHost, StringComparison.OrdinalIgnoreCase)
            && candidate.Port == baseUri.Port;
    }
}
