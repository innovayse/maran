using System.Globalization;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.ValueObjects;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Sites.Domain.Policies;

/// <summary>
/// Turns the settings a customer asked for into the settings a pool carries: each numeric value cut
/// down to the plan's bound, never raised (issue #138).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why clamp and not refuse or delete.</b> A plan edit can lower a bound below a value a customer
/// already chose. Refusing the pool write would take every site of the account off its settings;
/// deleting the row would lose the customer's choice for ever. Cutting the value at the pool and
/// keeping the row keeps both promises: the plan is never exceeded and raising the bound again gives
/// the wish back.
/// </para>
/// <para>
/// <b>The relation between the two sizes survives the cut.</b> <c>post_max_size</c> must not be below
/// <c>upload_max_filesize</c>, and both are bounded by the same upload figure, so clamping both to it
/// keeps the larger at least the smaller. A stored value the customer never could have chosen (above
/// the agent's own ceiling, from a hand-edited row) is cut by the plan's bound too, since a plan
/// bound is at or below the ceiling.
/// </para>
/// <para>
/// A value that is not a number the clamp can read passes through unchanged for a timezone and is
/// DROPPED for a numeric kind: a hand-edited row the panel would never have written must not turn
/// into a line the agent refuses for every site of the account.
/// </para>
/// </remarks>
public static class PhpSettingsPlanClamp
{
    /// <summary>Applies the plan's bounds to an account's stored settings.</summary>
    /// <param name="stored">The settings the customer asked for, in any order.</param>
    /// <param name="limits">The plan's bounds.</param>
    /// <returns>The settings the pool carries, in the order of <c>PhpSettingNames.All</c>.</returns>
    public static IReadOnlyList<PhpSettingValue> Apply(IEnumerable<PhpSetting> stored, PhpLimitsSnapshot limits)
    {
        var byName = stored.ToDictionary(setting => { return setting.Name; }, StringComparer.Ordinal);
        var effective = new List<PhpSettingValue>();
        foreach (var name in PhpSettingNames.All)
        {
            if (!byName.TryGetValue(name, out var setting))
            {
                continue;
            }

            var clamped = Clamp(name, setting.Value, limits);
            if (clamped is not null)
            {
                effective.Add(new PhpSettingValue(name, clamped));
            }
        }

        return effective;
    }

    /// <summary>Cuts one stored value to the plan's bound.</summary>
    /// <param name="name">The setting's name.</param>
    /// <param name="ini">The stored value in php.ini's spelling.</param>
    /// <param name="limits">The plan's bounds.</param>
    /// <returns>The value the pool carries, or null when a numeric value cannot be read.</returns>
    private static string? Clamp(string name, string ini, PhpLimitsSnapshot limits)
    {
        var kind = PhpSettingCatalogue.KindOf(name);
        var maximum = PhpSettingCatalogue.PlanMaximum(name, limits);
        if (maximum is null)
        {
            return ini;
        }

        if (!PhpSettingSpelling.TryReadNumber(PhpSettingSpelling.ToWire(kind, ini), out var number))
        {
            return null;
        }

        return PhpSettingSpelling.ToIni(kind, Math.Min(number, maximum.Value).ToString(CultureInfo.InvariantCulture));
    }
}
