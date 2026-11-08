using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.SitesService;

/// <summary>One redirect rule of a site, as the agent is told to render it.</summary>
/// <param name="Path">The path the rule applies to; starts with "/", and ends in "/" for a prefix rule.</param>
/// <param name="Match">How <paramref name="Path"/> is compared with the request path.</param>
/// <param name="Target">Where requests are sent: an absolute http(s) URL or a path on the same site.</param>
/// <param name="StatusCode">The HTTP status of the redirect: 301 or 302. The agent refuses any other.</param>
public sealed record RedirectRuleDescriptor(string Path, RedirectMatchKind Match, string Target, uint StatusCode)
{
    /// <summary>Converts this rule into its wire message.</summary>
    /// <returns>The populated <see cref="RedirectRule"/>.</returns>
    internal RedirectRule ToWire()
    {
        return new RedirectRule
        {
            Path = Path,
            Match = Match switch
            {
                RedirectMatchKind.Exact => RedirectMatch.Exact,
                RedirectMatchKind.Prefix => RedirectMatch.Prefix,
                _ => throw new ArgumentOutOfRangeException(nameof(Match), Match, "Unknown redirect match kind."),
            },
            Target = Target,
            Status = StatusCode,
        };
    }
}
