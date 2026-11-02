using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Domain.Enums;
using Maran.Modules.Monitoring.Resources;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Monitoring.Queries.ListServers;

/// <summary>
/// Handles <see cref="ListServersQuery"/>: the servers the panel manages, which today is exactly the
/// machine it runs on (issue #319).
/// </summary>
/// <remarks>
/// <para>
/// There is no registry to read yet. Fleet mode (issue #180) owns the registry, enrolment and the
/// transport to other servers; until it lands this handler answers the one fact that is true, and
/// invents no second entry. There is deliberately no "switch" operation beside it: one server leaves
/// nothing to switch, and an operation that cannot change anything would only be audited noise.
/// </para>
/// <para>
/// The name comes from <see cref="IPanelHostName"/>, the Sdk window onto the host name the panel is
/// configured to be reached by. When none is configured the entry carries no name instead of a
/// placeholder, so the interface cannot present a guess as a fact.
/// </para>
/// </remarks>
public sealed class ListServersQueryHandler
{
    /// <summary>The prefix of the display-name keys, one per <see cref="ServerKind"/> member.</summary>
    private const string KindKeyPrefix = "ServerKind";

    /// <summary>The window onto the panel's configured host name.</summary>
    private readonly IPanelHostName _panelHostName;

    /// <summary>The module's display names, resolved in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="panelHostName">The window onto the panel's configured host name.</param>
    /// <param name="displayNames">The module's display names.</param>
    public ListServersQueryHandler(IPanelHostName panelHostName, IStringLocalizer<DisplayNames> displayNames)
    {
        _panelHostName = panelHostName;
        _displayNames = displayNames;
    }

    /// <summary>Lists the managed servers.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <returns>The servers, always successfully — reading a configured name cannot fail.</returns>
    public Result<IReadOnlyList<ServerDto>> Handle(ListServersQuery query)
    {
        var hostName = _panelHostName.TryGet(out var configured) ? configured : null;
        var local = new ServerDto(ServerKind.Local, _displayNames[KindKeyPrefix + ServerKind.Local].Value, hostName);

        return Result<IReadOnlyList<ServerDto>>.Ok([local]);
    }
}
