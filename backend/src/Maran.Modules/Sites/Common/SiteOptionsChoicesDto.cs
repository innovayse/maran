namespace Maran.Modules.Sites.Common;

/// <summary>The closed sets the options screen selects from, labelled in the caller's language.</summary>
/// <param name="CanonicalHosts">The canonical-host choices, "none" included.</param>
/// <param name="RedirectMatches">How a redirect rule's path may be compared.</param>
/// <param name="RedirectStatuses">The statuses a redirect may answer with.</param>
/// <param name="IpAccessModes">Whether the IP rules allow or refuse the listed networks.</param>
public sealed record SiteOptionsChoicesDto(
    IReadOnlyList<SiteChoiceDto> CanonicalHosts,
    IReadOnlyList<SiteChoiceDto> RedirectMatches,
    IReadOnlyList<SiteChoiceDto> RedirectStatuses,
    IReadOnlyList<SiteChoiceDto> IpAccessModes);
