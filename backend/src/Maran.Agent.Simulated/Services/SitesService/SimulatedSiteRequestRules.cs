using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.Validation;

namespace Maran.Agent.Simulated.Services.SitesService;

/// <summary>
/// The agent's second look at the values of a site request: alphabets and sizes only, so that
/// nothing outside a plain name is stored and no list grows without bound.
/// </summary>
public static class SimulatedSiteRequestRules
{
    /// <summary>Whether every value of a create request is acceptable to the simulated agent.</summary>
    /// <param name="limits">The caps in force.</param>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="domain">The primary domain.</param>
    /// <param name="aliases">The additional host names.</param>
    /// <param name="kind">The backend that serves the content.</param>
    /// <param name="phpVersion">The PHP version; judged only for a PHP site.</param>
    /// <param name="proxyUpstream">The upstream; judged only for a reverse-proxy site.</param>
    /// <param name="settingOverrides">The php.ini overrides.</param>
    /// <returns>True when nothing is outside its alphabet or its cap.</returns>
    public static bool IsCreateWellFormed(
        SimulatedAgentLimits limits,
        string accountUsername,
        string domain,
        IReadOnlyList<string> aliases,
        SiteBackendKind kind,
        string phpVersion,
        string proxyUpstream,
        IReadOnlyList<PhpSettingDto> settingOverrides)
    {
        ArgumentNullException.ThrowIfNull(limits);

        var common = Enum.IsDefined(kind)
            && SimulatedInput.IsAccountName(accountUsername, limits.MaxAccountNameLength)
            && SimulatedInput.IsDomain(domain, limits.MaxDomainLength)
            && AreAliasesWellFormed(limits, aliases)
            && SimulatedPhpSettingRules.AreWellFormed(limits, settingOverrides);
        if (!common)
        {
            return false;
        }

        return kind switch
        {
            SiteBackendKind.Php => SimulatedInput.IsPhpVersion(phpVersion),
            SiteBackendKind.ReverseProxy => proxyUpstream.Length > 0
                && SimulatedInput.IsPlainText(proxyUpstream, limits.MaxProxyUpstreamLength),
            _ => true,
        };
    }

    /// <summary>Whether every alias is a well-formed host name and there are not more than the cap.</summary>
    /// <param name="limits">The caps in force.</param>
    /// <param name="aliases">The aliases a request named.</param>
    /// <returns>True when the list is acceptable.</returns>
    public static bool AreAliasesWellFormed(SimulatedAgentLimits limits, IReadOnlyList<string> aliases)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(aliases);

        return aliases.Count <= limits.MaxAliasesPerSite
            && aliases.All(alias => { return SimulatedInput.IsDomain(alias, limits.MaxDomainLength); });
    }
}
