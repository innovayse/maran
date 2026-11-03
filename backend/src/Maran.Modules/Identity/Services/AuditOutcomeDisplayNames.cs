using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Identity.Services;

/// <summary>Resolves the administrator-facing name of an audit outcome in the current request's culture.</summary>
/// <remarks>
/// <b>The name is the backend's to produce, not the SPA's</b> (rules/architecture.md "The backend owns
/// the data, the SPA renders it"). Unlike an audit action, the outcome set is closed and every member
/// has an entry, so a miss is a defect and not a fallback case: the module's tests walk
/// <c>Enum.GetValues</c> against the real resource files and fail on the first member without a name.
/// </remarks>
public sealed class AuditOutcomeDisplayNames
{
    /// <summary>The prefix an outcome's resx key carries, so outcomes cannot collide with other names.</summary>
    private const string KeyPrefix = "AuditOutcome";

    /// <summary>Resolves this module's display-name resources for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the resolver.</summary>
    /// <param name="displayNames">This module's display-name resources.</param>
    public AuditOutcomeDisplayNames(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Names one outcome as an administrator reads it.</summary>
    /// <param name="outcome">The outcome to name.</param>
    /// <returns>The localized name.</returns>
    public string Of(AuditOutcome outcome)
    {
        return _displayNames[KeyPrefix + outcome].Value;
    }
}
