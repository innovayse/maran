namespace Maran.Modules.Databases.Resources;

/// <summary>
/// Empty marker type naming <c>Resources/DisplayNames.resx</c> (+ <c>.ru</c>/<c>.hy</c>) for
/// <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> (rules/csharp.md "Resources
/// are reached through <c>IStringLocalizer&lt;T&gt;</c>"). Carries every user-facing name the
/// Databases module owns: <c>DatabasesModuleDisplayName</c> (resolved via
/// <see cref="DatabasesManifest"/>'s <c>DisplayNameKey</c>), and one
/// <c>GrantRepairRefusal&lt;Reason&gt;</c> plus one <c>GrantRepairAdvice&lt;Reason&gt;</c> entry per
/// reason the agent can refuse a grant-table row with.
/// </summary>
/// <remarks>
/// The refusal entries come in PAIRS on purpose, and the pairing is the requirement rather than a
/// convenience: naming a refusal tells an operator what the agent decided, and it is the advice that
/// tells them what to do about a row the panel will never touch again. A screen carrying only the
/// name would be a list of four verdicts with no next step, on rows that are frequently somebody
/// else's.
/// </remarks>
public sealed class DisplayNames
{
}
