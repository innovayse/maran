namespace Maran.Agent.Client.Services.SitesService;

/// <summary>Whether a site's listed networks are the only ones allowed or the ones refused.</summary>
/// <remarks>
/// A panel-side mirror of the wire's <c>IpAccessMode</c>. It has no "unspecified" member: a site with
/// no IP rules carries no <see cref="IpAccessDescriptor"/> at all, so there is nothing for it to describe.
/// </remarks>
public enum IpAccessKind
{
    /// <summary>Only the listed networks may reach the site; everyone else is refused.</summary>
    AllowList = 1,

    /// <summary>The listed networks are refused; everyone else may reach the site.</summary>
    DenyList = 2,
}
