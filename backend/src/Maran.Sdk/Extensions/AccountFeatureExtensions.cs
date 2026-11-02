using System.Numerics;
using Maran.Sdk.Contracts;

namespace Maran.Sdk.Extensions;

/// <summary>
/// Reads an <see cref="AccountFeature"/> set as the list of features it holds and builds one back,
/// so no module decodes bits itself.
/// </summary>
public static class AccountFeatureExtensions
{
    /// <summary>
    /// The features a set holds, one value per feature, in declaration order. <c>None</c> and
    /// <c>All</c> are never among them: they name sets, not features.
    /// </summary>
    /// <param name="set">The set to read.</param>
    /// <returns>The single-feature values whose bits are in <paramref name="set"/>.</returns>
    public static IReadOnlyList<AccountFeature> Members(this AccountFeature set)
    {
        return Enum.GetValues<AccountFeature>()
            .Where(candidate =>
            {
                return IsFeature(candidate) && set.HasFlag(candidate);
            })
            .ToList();
    }

    /// <summary>Whether a value names exactly one feature: a defined member that is neither <c>None</c> nor <c>All</c>.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><c>true</c> for <c>Mail</c>, <c>Dns</c> and the other single members; <c>false</c> for anything else.</returns>
    public static bool IsFeature(this AccountFeature value)
    {
        return value != AccountFeature.All
            && Enum.IsDefined(value)
            && BitOperations.PopCount((ulong)value) == 1;
    }

    /// <summary>Whether a value is a set the panel can hold: only bits of defined features are set.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><c>true</c> when no bit outside <c>All</c> is set.</returns>
    public static bool IsKnownSet(this AccountFeature value)
    {
        return (value & ~AccountFeature.All) == AccountFeature.None;
    }

    /// <summary>Combines a list of single features into the set they make.</summary>
    /// <param name="features">The features; an empty list makes the empty set, a repeated one is harmless.</param>
    /// <returns>The union of the features.</returns>
    public static AccountFeature ToSet(this IEnumerable<AccountFeature> features)
    {
        return features.Aggregate(AccountFeature.None, (set, feature) =>
        {
            return set | feature;
        });
    }
}
