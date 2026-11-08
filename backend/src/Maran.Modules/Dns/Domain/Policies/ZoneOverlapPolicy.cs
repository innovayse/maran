using Maran.Modules.Dns.Domain.Enums;

namespace Maran.Modules.Dns.Domain.Policies;

/// <summary>
/// Refuses a zone whose name would let one account answer for another account's names. The daemon answers
/// from the MOST SPECIFIC zone it holds, so account B holding <c>sub.example.com</c> beside account A's
/// <c>example.com</c> would publish B's data for a subtree of A's name, and the reverse would swallow B's.
/// </summary>
public static class ZoneOverlapPolicy
{
    /// <summary>Weighs a candidate apex against the zones other accounts own.</summary>
    /// <param name="candidate">The normalised apex being created.</param>
    /// <param name="foreignZoneNames">The normalised names of zones that belong to OTHER accounts.</param>
    /// <returns>
    /// <see cref="ZoneOverlapVerdict.Clear"/>, or the first collision found. The same account may nest its own
    /// zones, so the caller must not pass its own account's names.
    /// </returns>
    public static ZoneOverlapVerdict Check(string candidate, IEnumerable<string> foreignZoneNames)
    {
        foreach (var foreign in foreignZoneNames)
        {
            if (string.Equals(foreign, candidate, StringComparison.Ordinal))
            {
                return ZoneOverlapVerdict.SameAsForeignZone;
            }

            if (candidate.EndsWith("." + foreign, StringComparison.Ordinal))
            {
                return ZoneOverlapVerdict.ChildOfForeignZone;
            }

            if (foreign.EndsWith("." + candidate, StringComparison.Ordinal))
            {
                return ZoneOverlapVerdict.ParentOfForeignZone;
            }
        }

        return ZoneOverlapVerdict.Clear;
    }
}
