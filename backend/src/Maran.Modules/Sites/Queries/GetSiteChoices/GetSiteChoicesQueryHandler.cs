using System.Globalization;
using System.Text.Json;
using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Services;

namespace Maran.Modules.Sites.Queries.GetSiteChoices;

/// <summary>Handles <see cref="GetSiteChoicesQuery"/> from the module's own enums, policies and resource tables.</summary>
/// <remarks>
/// Server-wide reference data, not tenant data: the sets are the same for every account, so nothing
/// is read from the database and there is nothing to scope. What the host can serve (the installed
/// runtimes and the web server) comes from the installed apps through <see cref="SiteStackGate"/>. Each list is built from the enum the
/// validators and the entity use, so the options the SPA offers and the values the server accepts
/// cannot drift apart. A value's wire spelling is the camel-cased member name, the one the API
/// serializes the enum with; its label is the resource entry <c>SiteBackendType&lt;Member&gt;</c> or
/// <c>SiteStatus&lt;Member&gt;</c> in the caller's language.
/// </remarks>
public sealed class GetSiteChoicesQueryHandler
{
    /// <summary>Names the backends and serving states in the caller's culture.</summary>
    private readonly SiteDisplayNames _siteNames;

    /// <summary>Reads which runtimes and which web server are installed as apps.</summary>
    private readonly SiteStackGate _stack;

    /// <summary>Creates the handler.</summary>
    /// <param name="siteNames">Names the backends and serving states in the caller's culture.</param>
    /// <param name="stack">Reads which runtimes and which web server are installed as apps.</param>
    public GetSiteChoicesQueryHandler(SiteDisplayNames siteNames, SiteStackGate stack)
    {
        _siteNames = siteNames;
        _stack = stack;
    }

    /// <summary>
    /// Returns the backend types, serving states, traffic windows and log retentions, labelled in the
    /// caller's language. The windows and retentions come from the policies the validators use.
    /// </summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <param name="cancellationToken">Unused: nothing is read.</param>
    /// <returns>A successful result carrying the choices; this operation never fails.</returns>
    public async Task<Result<SiteChoicesDto>> HandleAsync(GetSiteChoicesQuery query, CancellationToken cancellationToken)
    {
        var installed = await _stack.ReadAsync(cancellationToken);
        var choices = new SiteChoicesDto(
            // A backend is offered only when the host can serve it: static and a reverse proxy need no
            // runtime, PHP and Node need an installed app. The server refuses the others at creation
            // (AppsNotInstalled) whatever this list says.
            ChoicesOf<SiteBackendType>(_siteNames.Of)
                .Where(choice =>
                {
                    return IsServable(choice.Value, installed);
                })
                .ToList(),
            ChoicesOf<SiteStatus>(_siteNames.Of),
            TrafficHistory.Windows.Select(days =>
            {
                return new SiteChoiceDto(days.ToString(CultureInfo.InvariantCulture), _siteNames.OfTrafficWindow(days));
            }).ToList(),
            Enumerable.Range(SiteLogRetention.MinimumDays, SiteLogRetention.MaximumDays - SiteLogRetention.MinimumDays + 1)
                .Select(days =>
                {
                    return new SiteChoiceDto(days.ToString(CultureInfo.InvariantCulture), _siteNames.OfLogRetention(days));
                })
                .ToList(),
            VersionChoices(installed.PhpVersions),
            VersionChoices(installed.NodeMajors),
            installed.WebServer is { } webServer ? WebServerChoice(webServer) : null);

        return Result<SiteChoicesDto>.Ok(choices);
    }

    /// <summary>Whether the host can serve a backend, given what is installed.</summary>
    /// <param name="wireValue">The backend's wire spelling.</param>
    /// <param name="installed">What is installed.</param>
    /// <returns>True for a backend that needs no runtime, or whose runtime is installed.</returns>
    private static bool IsServable(string wireValue, SiteHostApps installed)
    {
        if (wireValue == JsonNamingPolicy.CamelCase.ConvertName(nameof(SiteBackendType.Php)))
        {
            return installed.PhpVersions.Count > 0;
        }

        if (wireValue == JsonNamingPolicy.CamelCase.ConvertName(nameof(SiteBackendType.Node)))
        {
            return installed.NodeMajors.Count > 0;
        }

        return true;
    }

    /// <summary>Lists installed runtime versions as choices, newest first.</summary>
    /// <param name="versions">The installed versions.</param>
    /// <returns>One choice per version, valued and labelled by the version itself.</returns>
    private static List<SiteChoiceDto> VersionChoices(IReadOnlyList<string> versions)
    {
        return versions
            .OrderByDescending(OrderKeyOf)
            .Select(version =>
            {
                return new SiteChoiceDto(version, version);
            })
            .ToList();
    }

    /// <summary>Orders a runtime version numerically: "8.10" after "8.9", and a bare Node major as itself.</summary>
    /// <param name="version">A PHP version or a Node major.</param>
    /// <returns>The version as a comparable value; the lowest one for text that is not a version.</returns>
    private static Version OrderKeyOf(string version)
    {
        var padded = version.Contains('.', StringComparison.Ordinal) ? version : version + ".0";

        return Version.TryParse(padded, out var parsed) ? parsed : new Version(0, 0);
    }

    /// <summary>Names the installed web server as a choice.</summary>
    /// <param name="webServer">The installed web server.</param>
    /// <returns>The choice, valued by the lowercase product name.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a web server the mapping does not know.</exception>
    private static SiteChoiceDto WebServerChoice(SiteWebServer webServer)
    {
        return webServer switch
        {
            SiteWebServer.Nginx => new SiteChoiceDto("nginx", "nginx"),
            SiteWebServer.Apache => new SiteChoiceDto("apache", "Apache"),
            _ => throw new ArgumentOutOfRangeException(nameof(webServer), webServer, "Unmapped site web server."),
        };
    }

    /// <summary>Lists every member of an enum as a choice, in declaration order.</summary>
    /// <typeparam name="TEnum">The closed set to list.</typeparam>
    /// <param name="nameOf">Names one member in the caller's culture.</param>
    /// <returns>One choice per member.</returns>
    private static List<SiteChoiceDto> ChoicesOf<TEnum>(Func<TEnum, string> nameOf)
        where TEnum : struct, Enum
    {
        return Enum.GetValues<TEnum>().Select(member =>
        {
            return new SiteChoiceDto(JsonNamingPolicy.CamelCase.ConvertName(member.ToString()), nameOf(member));
        }).ToList();
    }
}
