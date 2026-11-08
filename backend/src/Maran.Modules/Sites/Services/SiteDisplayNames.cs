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

    /// <summary>The prefix a canonical-host choice's resx key carries.</summary>
    private const string CanonicalHostPrefix = "SiteCanonicalHost";

    /// <summary>The prefix a redirect match's resx key carries.</summary>
    private const string RedirectMatchPrefix = "SiteRedirectMatch";

    /// <summary>The prefix a redirect status's resx key carries.</summary>
    private const string RedirectStatusPrefix = "SiteRedirectStatus";

    /// <summary>The prefix an IP access mode's resx key carries.</summary>
    private const string IpAccessModePrefix = "SiteIpAccessMode";

    /// <summary>The prefix a traffic-window label's resx key carries.</summary>
    private const string TrafficWindowKey = "SiteTrafficWindowDays";

    /// <summary>The prefix an HTTP status class's resx key carries.</summary>
    private const string StatusClassPrefix = "SiteTrafficStatusClass";

    /// <summary>The prefix a PHP support status's resx key carries.</summary>
    private const string PhpSupportStatusPrefix = "PhpSupportStatus";

    /// <summary>The prefix an extension availability's resx key carries.</summary>
    private const string ExtensionAvailabilityPrefix = "ExtensionAvailability";

    /// <summary>The prefix a PHP extension's resx key carries; the catalogue name is appended in PascalCase.</summary>
    private const string PhpExtensionPrefix = "PhpExtension";

    /// <summary>The key naming a retention of exactly one day.</summary>
    private const string RetentionOneDayKey = "SiteLogRetentionOneDay";

    /// <summary>The key naming a retention of two to four days, for languages that decline the noun.</summary>
    private const string RetentionFewDaysKey = "SiteLogRetentionFewDays";

    /// <summary>The key naming a retention of five days or more.</summary>
    private const string RetentionManyDaysKey = "SiteLogRetentionManyDays";

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

    /// <summary>Names a canonical-host choice, as a customer reads it.</summary>
    /// <param name="canonicalHost">The choice to name.</param>
    /// <returns>The localized name.</returns>
    public string Of(CanonicalHost canonicalHost)
    {
        return _displayNames[CanonicalHostPrefix + canonicalHost].Value;
    }

    /// <summary>Names how a redirect rule's path is compared, as a customer reads it.</summary>
    /// <param name="redirectMatch">The comparison to name.</param>
    /// <returns>The localized name.</returns>
    public string Of(RedirectMatch redirectMatch)
    {
        return _displayNames[RedirectMatchPrefix + redirectMatch].Value;
    }

    /// <summary>Names the status a redirect answers with, as a customer reads it.</summary>
    /// <param name="redirectStatus">The status to name.</param>
    /// <returns>The localized name.</returns>
    public string Of(RedirectStatus redirectStatus)
    {
        return _displayNames[RedirectStatusPrefix + redirectStatus].Value;
    }

    /// <summary>Names whether IP rules allow or refuse the listed networks, as a customer reads it.</summary>
    /// <param name="ipAccessMode">The mode to name.</param>
    /// <returns>The localized name.</returns>
    public string Of(IpAccessMode ipAccessMode)
    {
        return _displayNames[IpAccessModePrefix + ipAccessMode].Value;
    }

    /// <summary>Names where a PHP version stands in its support life, as an operator reads it.</summary>
    /// <param name="status">The status to name.</param>
    /// <returns>The localized name.</returns>
    public string Of(PhpSupportStatus status)
    {
        return _displayNames[PhpSupportStatusPrefix + status].Value;
    }

    /// <summary>Names what the host can do with a PHP extension, as an administrator reads it.</summary>
    /// <param name="availability">The availability to name.</param>
    /// <returns>The localized name, such as "Can be switched on and off".</returns>
    public string Of(ExtensionAvailability availability)
    {
        return _displayNames[ExtensionAvailabilityPrefix + availability].Value;
    }

    /// <summary>Names one catalogue PHP extension, as an operator reads it.</summary>
    /// <param name="extension">The catalogue name, lowercase, e.g. <c>gd</c>.</param>
    /// <returns>The localized name, such as "Images (GD)".</returns>
    public string OfPhpExtension(string extension)
    {
        return _displayNames[PhpExtensionPrefix + char.ToUpperInvariant(extension[0]) + extension[1..]].Value;
    }

    /// <summary>Names a period the traffic summary may be read over.</summary>
    /// <param name="days">The period's length in days.</param>
    /// <returns>The localized name, such as "Last 7 days".</returns>
    public string OfTrafficWindow(int days)
    {
        return _displayNames[TrafficWindowKey, days].Value;
    }

    /// <summary>Names a number of days a site may keep its logs.</summary>
    /// <param name="days">The number of days.</param>
    /// <returns>
    /// The localized name, declined for the number where the language declines it: one day, two to four
    /// days, five or more.
    /// </returns>
    public string OfLogRetention(int days)
    {
        var key = days switch
        {
            1 => RetentionOneDayKey,
            >= 2 and <= 4 => RetentionFewDaysKey,
            _ => RetentionManyDaysKey,
        };

        return _displayNames[key, days].Value;
    }

    /// <summary>Names a class of HTTP status codes, such as the 4xx ones.</summary>
    /// <param name="hundreds">The class: the status code divided by 100, from 1 to 5.</param>
    /// <returns>The localized name, such as "4xx client error".</returns>
    public string OfStatusClass(int hundreds)
    {
        return _displayNames[StatusClassPrefix + hundreds].Value;
    }
}
