using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Domain.ValueObjects;
using Maran.Modules.Apps.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Apps.Services;

/// <summary>Resolves the names of an app, its kind, its ledger state, its service status and the reason Node.js is missing in the current request's culture.</summary>
/// <remarks>
/// <b>The names are the backend's to produce, not the SPA's</b> (rules/architecture.md "The backend owns
/// the data, the SPA renders it"). Every set is closed and every member has an entry, so a miss is a defect
/// and not a fallback case. The app list, the installable list and the first-run screen draw from this one
/// resolver, so the word a picker offers and the word a badge prints cannot differ.
/// </remarks>
public sealed class AppDisplayNames
{
    /// <summary>The prefix a kind's resx key carries.</summary>
    private const string KindPrefix = "AppKind";

    /// <summary>The prefix a ledger state's resx key carries.</summary>
    private const string StatePrefix = "AppState";

    /// <summary>The prefix a service status's resx key carries.</summary>
    private const string ServiceStatusPrefix = "AppServiceStatus";

    /// <summary>The prefix a Node.js unavailability reason's resx key carries.</summary>
    private const string NodejsUnavailablePrefix = "NodejsUnavailable";

    /// <summary>The prefix an app family's resx key carries.</summary>
    private const string FamilyPrefix = "AppFamily";

    /// <summary>Resolves this module's display-name resources for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the resolver.</summary>
    /// <param name="displayNames">This module's display names.</param>
    public AppDisplayNames(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Names an app, e.g. "nginx", "PHP 8.3" or "Node.js 22".</summary>
    /// <param name="appId">The catalogue id.</param>
    /// <returns>The name in the request's language.</returns>
    public string Of(AppId appId)
    {
        return _displayNames[FamilyPrefix + appId.Family, appId.Version].Value;
    }

    /// <summary>Names an app kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The name in the request's language.</returns>
    public string Of(AppKind kind)
    {
        return _displayNames[KindPrefix + kind].Value;
    }

    /// <summary>Names a ledger state.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The name in the request's language.</returns>
    public string Of(AppState state)
    {
        return _displayNames[StatePrefix + state].Value;
    }

    /// <summary>Names a service status.</summary>
    /// <param name="status">The status.</param>
    /// <returns>The name in the request's language.</returns>
    public string Of(AppServiceStatus status)
    {
        return _displayNames[ServiceStatusPrefix + status].Value;
    }

    /// <summary>Names why the Node.js apps are missing from the installable list (issue #532).</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>The sentence in the request's language.</returns>
    public string Of(NodejsUnavailableReason reason)
    {
        return _displayNames[NodejsUnavailablePrefix + reason].Value;
    }
}
