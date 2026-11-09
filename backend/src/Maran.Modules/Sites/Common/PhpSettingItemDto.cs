namespace Maran.Modules.Sites.Common;

/// <summary>One customer PHP setting as a screen shows it: what it is, what was chosen, what the pool carries.</summary>
/// <remarks>
/// Values are in the wire spelling: a size is its number of mebibytes (<c>256</c>), seconds and counts are
/// digits, a timezone is the zone name. The panel converts to and from php.ini's spelling, so a screen
/// never builds or parses a unit suffix.
/// </remarks>
/// <param name="Name">The setting's name as php.ini spells it, e.g. <c>memory_limit</c>.</param>
/// <param name="Label">The setting's name in the caller's language.</param>
/// <param name="Kind">How the value is written: <c>size</c>, <c>seconds</c>, <c>count</c> or <c>timezone</c>.</param>
/// <param name="Unit">The unit word in the caller's language (MiB, seconds); null for a count and a timezone.</param>
/// <param name="Value">What the customer chose; null when they never set it and PHP's own default applies.</param>
/// <param name="EffectiveValue">What the pool carries after the plan's bound; null when no figure is carried.</param>
/// <param name="PlanMaximum">The plan's largest figure for the setting, in its unit; null for a timezone.</param>
/// <param name="ExceedsPlan">True when the chosen value is above the plan's bound, so the bound applies instead.</param>
/// <param name="Choices">The zone names the server knows, for a timezone; null for every other kind.</param>
public sealed record PhpSettingItemDto(
    string Name,
    string Label,
    string Kind,
    string? Unit,
    string? Value,
    string? EffectiveValue,
    int? PlanMaximum,
    bool ExceedsPlan,
    IReadOnlyList<string>? Choices);
