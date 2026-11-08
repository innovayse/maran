using Maran.Modules.Sites.Domain.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Sites.Domain.Policies;

/// <summary>
/// What each customer PHP setting is: its kind, its absolute ceiling and the plan bound that governs
/// it (issue #138). The panel's side of the agent's whitelist, kept in one place for the validator,
/// the clamp and the screen's description.
/// </summary>
/// <remarks>
/// Advice here, authority in the agent: the agent re-derives its own table and refuses what it does
/// not know (rules/security.md item 1), so a name or ceiling that drifted would show the customer an
/// error and never write a line the agent did not choose to accept.
/// </remarks>
public static class PhpSettingCatalogue
{
    /// <summary>Whether <paramref name="name"/> is a setting a customer may change.</summary>
    /// <param name="name">The name as the caller spelled it; compared exactly.</param>
    /// <returns>True for a name in <see cref="PhpSettingNames.All"/>.</returns>
    public static bool IsKnown(string name)
    {
        return PhpSettingNames.All.Contains(name, StringComparer.Ordinal);
    }

    /// <summary>The kind of a known setting.</summary>
    /// <param name="name">A name for which <see cref="IsKnown"/> is true.</param>
    /// <returns>How the setting's value is written and bounded.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for a name that is not on the list.</exception>
    public static PhpSettingKind KindOf(string name)
    {
        return name switch
        {
            PhpSettingNames.MemoryLimit => PhpSettingKind.Size,
            PhpSettingNames.UploadMaxFilesize => PhpSettingKind.Size,
            PhpSettingNames.PostMaxSize => PhpSettingKind.Size,
            PhpSettingNames.MaxExecutionTime => PhpSettingKind.Seconds,
            PhpSettingNames.MaxInputVars => PhpSettingKind.Count,
            PhpSettingNames.DateTimezone => PhpSettingKind.Timezone,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Not a customer PHP setting."),
        };
    }

    /// <summary>The largest figure the agent will ever write for a numeric setting, in the setting's unit.</summary>
    /// <param name="name">A name for which <see cref="IsKnown"/> is true.</param>
    /// <returns>The agent's absolute ceiling, or null for a setting with no figure (the timezone).</returns>
    public static int? AbsoluteMaximum(string name)
    {
        return name switch
        {
            PhpSettingNames.MemoryLimit => PhpSettingCeilings.MaxSizeMb,
            PhpSettingNames.UploadMaxFilesize => PhpSettingCeilings.MaxSizeMb,
            PhpSettingNames.PostMaxSize => PhpSettingCeilings.MaxSizeMb,
            PhpSettingNames.MaxExecutionTime => PhpSettingCeilings.MaxExecutionSeconds,
            PhpSettingNames.MaxInputVars => PhpSettingCeilings.MaxInputVars,
            _ => null,
        };
    }

    /// <summary>The largest figure a plan allows for a numeric setting, in the setting's unit.</summary>
    /// <param name="name">A name for which <see cref="IsKnown"/> is true.</param>
    /// <param name="limits">The plan's bounds.</param>
    /// <returns>
    /// The plan's maximum, or null for a setting no bound governs (the timezone). One upload bound
    /// governs both <c>upload_max_filesize</c> and <c>post_max_size</c>, because a body smaller than
    /// the file it carries silently loses the file.
    /// </returns>
    public static int? PlanMaximum(string name, PhpLimitsSnapshot limits)
    {
        return name switch
        {
            PhpSettingNames.MemoryLimit => limits.MemoryLimitMaxMb,
            PhpSettingNames.UploadMaxFilesize => limits.UploadMaxMb,
            PhpSettingNames.PostMaxSize => limits.UploadMaxMb,
            PhpSettingNames.MaxExecutionTime => limits.MaxExecutionSeconds,
            PhpSettingNames.MaxInputVars => limits.MaxInputVars,
            _ => null,
        };
    }
}
