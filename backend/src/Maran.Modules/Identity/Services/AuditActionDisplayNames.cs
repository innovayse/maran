using Maran.Modules.Identity.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Resolves the operator-facing name of an audit action in the current request's culture.
/// </summary>
/// <remarks>
/// <para>
/// <b>The name is the backend's to produce, not the SPA's.</b> Every domain value the interface
/// shows is produced and localized here (rules/architecture.md "The backend owns the data, the SPA
/// renders it"); before this existed the audit screen printed the machine constant, so an operator
/// read <c>BackupRestored</c> in every language the panel speaks. Built in the same shape as the
/// Tasks module's kind names and the Backups module's failure names, deliberately, so the panel has
/// one way of naming a stored code rather than three.
/// </para>
/// <para>
/// <b>The machine action stays on the wire beside the name.</b> This type adds a reading; it
/// replaces nothing. The journal's rows and the DTO keep the constant an administrator greps a log
/// or quotes in a ticket by, and the screen shows both.
/// </para>
/// <para>
/// <b>The key scheme lives in this one method and the fallback is the action itself.</b>
/// <c>AuditActions</c> is deliberately a set of string constants rather than an enum, because a
/// marketplace module records actions this assembly was never compiled knowing about — so a resx
/// entry cannot be guaranteed to exist. <see cref="IStringLocalizer"/> answers a missing key with
/// the key, which would put <c>AuditActionWhatever</c> on the screen, so the miss is detected and
/// the raw action is returned instead: exactly what the screen showed before, for actions nobody
/// has translated, and a real name for every action that ships here. The module's tests walk the
/// whole of <c>AuditActions</c> — the closed set every shipped producer records from — and fail on
/// the first constant that has no entry, so the fallback covers a marketplace module's action and
/// nothing this repository writes.
/// </para>
/// </remarks>
public sealed class AuditActionDisplayNames
{
    /// <summary>The prefix an action's resx key carries, so actions cannot collide with other names.</summary>
    private const string KeyPrefix = "AuditAction";

    /// <summary>Resolves this module's display-name resources for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the resolver.</summary>
    /// <param name="displayNames">This module's display-name resources.</param>
    public AuditActionDisplayNames(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Names one audit action as an operator reads it.</summary>
    /// <param name="action">The machine-stable action, from <c>AuditActions</c>.</param>
    /// <returns>
    /// The localized name, or <paramref name="action"/> itself when this build carries no entry for it.
    /// </returns>
    public string Of(string action)
    {
        var localized = _displayNames[KeyPrefix + action];

        return localized.ResourceNotFound ? action : localized.Value;
    }
}
