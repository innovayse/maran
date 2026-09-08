using Maran.Modules.Tasks.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Tasks.Services;

/// <summary>
/// Resolves the operator-facing name of a task kind in the current request's culture.
/// </summary>
/// <remarks>
/// <para>
/// <b>The name is the backend's to produce, not the SPA's.</b> Every domain value the interface
/// shows is produced and localized here (rules/architecture.md "The backend owns the data, the SPA
/// renders it"); before this existed the tasks screen printed the machine constant, so an operator
/// read <c>BackupRestore</c> in every language the panel speaks.
/// </para>
/// <para>
/// <b>The key scheme lives in this one method and the fallback is the kind itself.</b>
/// <c>TaskKinds</c> is deliberately a set of string constants rather than an enum, because a
/// marketplace module records kinds this assembly was never compiled knowing about — so a resx entry
/// cannot be guaranteed to exist. <see cref="IStringLocalizer"/> answers a missing key with the key,
/// which would put <c>TaskKindWhatever</c> on the screen, so the miss is detected and the raw kind is
/// returned instead: exactly what the screen showed before, for kinds nobody has translated, and a
/// real name for every kind that ships here.
/// </para>
/// </remarks>
public sealed class TaskKindDisplayNames
{
    /// <summary>The prefix a task kind's resx key carries, so kinds cannot collide with other names.</summary>
    private const string KeyPrefix = "TaskKind";

    /// <summary>Resolves this module's display-name resources for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the resolver.</summary>
    /// <param name="displayNames">This module's display-name resources.</param>
    public TaskKindDisplayNames(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Names one task kind as an operator reads it.</summary>
    /// <param name="kind">The machine-stable kind, from <c>TaskKinds</c>.</param>
    /// <returns>
    /// The localized name, or <paramref name="kind"/> itself when this build carries no entry for it.
    /// </returns>
    public string Of(string kind)
    {
        var localized = _displayNames[KeyPrefix + kind];

        return localized.ResourceNotFound ? kind : localized.Value;
    }
}
