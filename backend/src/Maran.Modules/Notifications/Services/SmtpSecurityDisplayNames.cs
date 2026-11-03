using Maran.Modules.Notifications.Domain.Enums;
using Maran.Modules.Notifications.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Notifications.Services;

/// <summary>Resolves the administrator-facing name of an SMTP protection mode in the current request's culture.</summary>
/// <remarks>
/// <para>
/// <b>The name is the backend's to produce, not the SPA's.</b> The mode list is a closed set the
/// panel owns, and the form that picks from it used to word the three choices itself, so the SPA held
/// a copy of the vocabulary that nothing tied to the enum (rules/architecture.md "The backend owns
/// the data, the SPA renders it").
/// </para>
/// <para>
/// <b>Unlike a task kind or an audit action, the set is closed and every member has an entry.</b> A
/// miss therefore means a member was added without its words, which is a defect and not a fallback
/// case: the module's tests walk <c>Enum.GetValues</c> against the real resource files and fail on
/// the first member without a name in any of the three languages.
/// </para>
/// </remarks>
public sealed class SmtpSecurityDisplayNames
{
    /// <summary>The prefix a mode's resx key carries, so modes cannot collide with other names.</summary>
    private const string KeyPrefix = "SmtpSecurity";

    /// <summary>Resolves this module's display-name resources for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the resolver.</summary>
    /// <param name="displayNames">This module's display-name resources.</param>
    public SmtpSecurityDisplayNames(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Names one protection mode as an administrator reads it.</summary>
    /// <param name="security">The mode to name.</param>
    /// <returns>The localized name.</returns>
    public string Of(SmtpSecurity security)
    {
        return _displayNames[KeyPrefix + security].Value;
    }
}
