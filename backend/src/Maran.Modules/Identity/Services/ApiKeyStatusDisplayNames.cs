using Maran.Modules.Identity.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Resolves the administrator-facing name of an API key status in the current request's culture.
/// </summary>
/// <remarks>
/// The status's resx key is <c>ApiKeyStatus</c> followed by the lower-case filter value with its first
/// letter capitalised (<c>active</c> is <c>ApiKeyStatusActive</c>). A status this build carries no
/// entry for is named by itself, which is exactly what the screen would otherwise have to print.
/// </remarks>
public sealed class ApiKeyStatusDisplayNames
{
    /// <summary>The prefix a status's resx key carries, so statuses cannot collide with other names.</summary>
    private const string KeyPrefix = "ApiKeyStatus";

    /// <summary>Resolves this module's display-name resources for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the resolver.</summary>
    /// <param name="displayNames">This module's display-name resources.</param>
    public ApiKeyStatusDisplayNames(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Names one status as an administrator reads it.</summary>
    /// <param name="status">The machine-stable status, from <c>ApiKeyStatusFilters</c>.</param>
    /// <returns>The localized name, or <paramref name="status"/> itself when this build carries no entry for it.</returns>
    public string Of(string status)
    {
        var localized = _displayNames[KeyPrefix + char.ToUpperInvariant(status[0]) + status[1..]];

        return localized.ResourceNotFound ? status : localized.Value;
    }
}
