namespace Maran.Modules.Sites.Domain.ValueObjects;

/// <summary>
/// One php.ini setting as a pool is told it: a whitelisted name and a value in php.ini's spelling.
/// </summary>
/// <remarks>
/// It is what a stored <see cref="Entities.PhpSetting"/> becomes after the plan's bounds are applied
/// (<see cref="Policies.PhpSettingsPlanClamp"/>), so a value of this type is the figure a pool carries,
/// never the figure a customer asked for.
/// </remarks>
/// <param name="Name">The setting's name, one of <c>PhpSettingNames</c>.</param>
/// <param name="Value">The value in php.ini's spelling (<c>256M</c>, <c>120</c>, <c>Europe/Berlin</c>).</param>
public sealed record PhpSettingValue(string Name, string Value);
