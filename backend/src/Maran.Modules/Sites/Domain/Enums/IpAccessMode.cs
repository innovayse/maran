namespace Maran.Modules.Sites.Domain.Enums;

/// <summary>Whether a site's listed networks are the only ones allowed or the ones refused.</summary>
public enum IpAccessMode
{
    /// <summary>Only the listed networks may reach the site; everyone else is refused.</summary>
    AllowList = 1,

    /// <summary>The listed networks are refused; everyone else may reach the site.</summary>
    DenyList = 2,
}
