namespace Maran.Modules.Sites.Common;

/// <summary>
/// The closed sets the sites screens select from, read from the server rather than written into the
/// SPA (rules/architecture.md "The backend owns the data, the SPA renders it"). The server keeps
/// validating every submitted value against the same enums these lists are built from.
/// </summary>
/// <param name="BackendTypes">
/// What can serve a site, in the order the form offers them. Only what the host can actually serve:
/// PHP appears when a PHP version is installed as an app, Node when a Node major is.
/// </param>
/// <param name="Statuses">Whether a site serves its own content, in the order the filter offers them.</param>
/// <param name="TrafficWindows">The periods the traffic summary may be read over, the one to open on first.</param>
/// <param name="LogRetentions">The numbers of days a site may keep its logs, fewest first.</param>
/// <param name="PhpVersions">The PHP versions installed as apps, newest first; empty when there are none.</param>
/// <param name="NodeVersions">The Node majors installed as apps, newest first; empty when there are none.</param>
/// <param name="WebServer">The installed site web server every new site is rendered for, or null when none is installed.</param>
public sealed record SiteChoicesDto(
    IReadOnlyList<SiteChoiceDto> BackendTypes,
    IReadOnlyList<SiteChoiceDto> Statuses,
    IReadOnlyList<SiteChoiceDto> TrafficWindows,
    IReadOnlyList<SiteChoiceDto> LogRetentions,
    IReadOnlyList<SiteChoiceDto> PhpVersions,
    IReadOnlyList<SiteChoiceDto> NodeVersions,
    SiteChoiceDto? WebServer);
