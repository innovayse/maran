using Maran.Modules.Databases.Domain.Enums;

namespace Maran.Modules.Databases.Domain.Policies;

/// <summary>
/// The one place a list of <see cref="DatabasePrivilege"/> values becomes a <see cref="DatabasePrivilegeSet"/>
/// and back: every bit operation on a stored privilege set lives here.
/// </summary>
public static class DatabasePrivilegeSets
{
    /// <summary>Every privilege the panel can grant, in the order the screen lists them.</summary>
    public static readonly IReadOnlyList<DatabasePrivilege> All = Enum.GetValues<DatabasePrivilege>()
        .OrderBy(privilege =>
        {
            return (int)privilege;
        })
        .ToList();

    /// <summary>The set holding every privilege the panel can grant.</summary>
    public static DatabasePrivilegeSet Full
    {
        get
        {
            return Of(All);
        }
    }

    /// <summary>Builds the set holding exactly <paramref name="privileges"/>.</summary>
    /// <param name="privileges">The privileges to hold; a repeated one counts once.</param>
    /// <returns>The set, <see cref="DatabasePrivilegeSet.None"/> for an empty list.</returns>
    public static DatabasePrivilegeSet Of(IEnumerable<DatabasePrivilege> privileges)
    {
        ArgumentNullException.ThrowIfNull(privileges);

        var set = DatabasePrivilegeSet.None;

        foreach (var privilege in privileges)
        {
            set |= BitOf(privilege);
        }

        return set;
    }

    /// <summary>Lists the privileges <paramref name="set"/> holds, in the order the screen lists them.</summary>
    /// <param name="set">The set to read.</param>
    /// <returns>The held privileges, empty for <see cref="DatabasePrivilegeSet.None"/>.</returns>
    public static IReadOnlyList<DatabasePrivilege> MembersOf(DatabasePrivilegeSet set)
    {
        return All
            .Where(privilege =>
            {
                return (set & BitOf(privilege)) != DatabasePrivilegeSet.None;
            })
            .ToList();
    }

    /// <summary>Whether <paramref name="set"/> holds only bits the panel defines.</summary>
    /// <param name="set">The set to check, as read from a stored column.</param>
    /// <returns>False when a bit above the last defined privilege is set.</returns>
    public static bool IsKnown(DatabasePrivilegeSet set)
    {
        return (set & ~Full) == DatabasePrivilegeSet.None;
    }

    /// <summary>The single bit of one privilege.</summary>
    /// <param name="privilege">The privilege.</param>
    /// <returns>The set holding that privilege alone.</returns>
    private static DatabasePrivilegeSet BitOf(DatabasePrivilege privilege)
    {
        return (DatabasePrivilegeSet)(1 << (int)privilege);
    }
}
