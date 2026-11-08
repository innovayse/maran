namespace Maran.Sdk.Contracts;

/// <summary>Whether a site's listed networks are the only ones allowed or the ones refused, as one module reports it to another.</summary>
/// <remarks>A deliberate second declaration of a closed set the Sites module owns, for the reason <see cref="SiteBackend"/> gives.</remarks>
public enum SiteIpAccessMode
{
    /// <summary>Only the listed networks may reach the site; everyone else is refused.</summary>
    AllowList = 1,

    /// <summary>The listed networks are refused; everyone else may reach the site.</summary>
    DenyList = 2,
}
