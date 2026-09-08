using Maran.Modules.Backups.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Backups.Services;

/// <summary>
/// Resolves the operator-facing name of a backup's failure code in the current request's culture.
/// </summary>
/// <remarks>
/// <para>
/// <b>The name is the backend's to produce, not the SPA's.</b> Every domain value the interface
/// shows is produced and localized here (rules/architecture.md "The backend owns the data, the SPA
/// renders it"); before this existed the backups table printed the machine constant beside a
/// translated word, so an operator read <c>Не удалась AgentSystemFailure</c>. Built in the same
/// shape as the Tasks module's kind names, deliberately, so the panel has one way of naming a
/// stored code rather than two.
/// </para>
/// <para>
/// <b>The codes come from two assemblies and the names live in one.</b> A failed row records either
/// one of this module's own codes or the code the agent client translated the agent's refusal into,
/// and a screen cannot tell them apart. Prefixing the key with <c>BackupFailure</c> is what lets
/// both sets be named here without a name colliding with anything else this module shows, and
/// without this module reaching into the agent client's resources for text written for a different
/// purpose — those sentences are error messages addressed to whoever attempted the operation, and
/// what a table cell needs is a short phrase in the past tense.
/// </para>
/// <para>
/// <b>The fallback is the code itself, and it is a fallback rather than a promise.</b> A build that
/// gained a failure code and no entry for it shows exactly what the table showed before this type
/// existed. That miss is not left to chance: the module's tests walk both closed sets the codes come
/// from — every create-stream ending, and every wire error code — and fail on the first that has no
/// entry, so the fallback covers a marketplace module's code and nothing that ships here.
/// </para>
/// </remarks>
public sealed class BackupFailureDisplayNames
{
    /// <summary>The prefix a failure code's resx key carries, so codes cannot collide with other names.</summary>
    private const string KeyPrefix = "BackupFailure";

    /// <summary>Resolves this module's display-name resources for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the resolver.</summary>
    /// <param name="displayNames">This module's display-name resources.</param>
    public BackupFailureDisplayNames(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Names one failure code as an operator reads it.</summary>
    /// <param name="failureCode">
    /// The machine-stable code recorded on the row, or the empty string when nothing failed.
    /// </param>
    /// <returns>
    /// The localized name; the empty string when there is no failure to name, and
    /// <paramref name="failureCode"/> itself when this build carries no entry for it.
    /// </returns>
    public string Of(string failureCode)
    {
        if (string.IsNullOrEmpty(failureCode))
        {
            return string.Empty;
        }

        var localized = _displayNames[KeyPrefix + failureCode];

        return localized.ResourceNotFound ? failureCode : localized.Value;
    }
}
