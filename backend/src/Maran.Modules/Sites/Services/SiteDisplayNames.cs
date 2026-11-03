using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Sites.Services;

/// <summary>Resolves the operator-facing names of a site's backend and serving state in the current request's culture.</summary>
/// <remarks>
/// <b>The names are the backend's to produce, not the SPA's</b> (rules/architecture.md "The backend
/// owns the data, the SPA renders it"). Both sets are closed and every member has an entry, so a miss
/// is a defect and not a fallback case. The choices endpoint and the site views draw their labels
/// from this one resolver, so the word a picker offers and the word a badge prints cannot differ.
/// </remarks>
public sealed class SiteDisplayNames
{
    /// <summary>The prefix a backend type's resx key carries.</summary>
    private const string BackendTypePrefix = "SiteBackendType";

    /// <summary>The prefix a serving state's resx key carries.</summary>
    private const string StatusPrefix = "SiteStatus";

    /// <summary>Resolves this module's display-name resources for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the resolver.</summary>
    /// <param name="displayNames">This module's display-name resources.</param>
    public SiteDisplayNames(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Names what serves a site's content, as an operator reads it.</summary>
    /// <param name="backendType">The backend to name.</param>
    /// <returns>The localized name.</returns>
    public string Of(SiteBackendType backendType)
    {
        return _displayNames[BackendTypePrefix + backendType].Value;
    }

    /// <summary>Names a site's serving state, as an operator reads it.</summary>
    /// <param name="status">The state to name.</param>
    /// <returns>The localized name.</returns>
    public string Of(SiteStatus status)
    {
        return _displayNames[StatusPrefix + status].Value;
    }
}
