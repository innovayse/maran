using Maran.Modules.Databases.Domain.Enums;
using Maran.Modules.Databases.Domain.Policies;

namespace Maran.Modules.Databases.Tests.Domain.Policies;

/// <summary>The one place a list of privileges becomes a stored set and back.</summary>
public sealed class DatabasePrivilegeSetsTests
{
    /// <summary>The closed list holds exactly eighteen privileges, each with its own bit.</summary>
    [Fact]
    public void The_closed_list_holds_eighteen_privileges_each_with_its_own_bit()
    {
        Assert.Equal(18, DatabasePrivilegeSets.All.Count);

        var single = DatabasePrivilegeSets.All
            .Select(privilege => { return DatabasePrivilegeSets.Of([privilege]); })
            .ToList();
        Assert.Equal(18, single.Distinct().Count());
        Assert.All(single, set => { Assert.Equal(1, int.PopCount((int)set)); });
    }

    /// <summary>The numbering of the wire enum is the bit position the stored set uses.</summary>
    [Theory]
    [InlineData(DatabasePrivilege.Select, DatabasePrivilegeSet.Select)]
    [InlineData(DatabasePrivilege.CreateTemporaryTables, DatabasePrivilegeSet.CreateTemporaryTables)]
    [InlineData(DatabasePrivilege.Event, DatabasePrivilegeSet.Event)]
    public void A_privilege_maps_to_the_set_member_of_the_same_name(DatabasePrivilege privilege, DatabasePrivilegeSet set)
    {
        Assert.Equal(set, DatabasePrivilegeSets.Of([privilege]));
        Assert.Equal(privilege.ToString(), set.ToString());
    }

    /// <summary>Building a set and reading it back gives the same privileges in the screen's order.</summary>
    [Fact]
    public void Building_a_set_and_reading_it_back_gives_the_same_privileges_in_the_screens_order()
    {
        var set = DatabasePrivilegeSets.Of([DatabasePrivilege.Event, DatabasePrivilege.Select, DatabasePrivilege.Drop]);

        Assert.Equal(
            [DatabasePrivilege.Select, DatabasePrivilege.Drop, DatabasePrivilege.Event],
            DatabasePrivilegeSets.MembersOf(set));
    }

    /// <summary>A repeated privilege counts once.</summary>
    [Fact]
    public void A_repeated_privilege_counts_once()
    {
        Assert.Equal(
            DatabasePrivilegeSet.Select,
            DatabasePrivilegeSets.Of([DatabasePrivilege.Select, DatabasePrivilege.Select]));
    }

    /// <summary>An empty list is the empty set and reads back as no privileges.</summary>
    [Fact]
    public void An_empty_list_is_the_empty_set_and_reads_back_as_no_privileges()
    {
        Assert.Equal(DatabasePrivilegeSet.None, DatabasePrivilegeSets.Of([]));
        Assert.Empty(DatabasePrivilegeSets.MembersOf(DatabasePrivilegeSet.None));
    }

    /// <summary>The full set holds every privilege and a bit beyond them is not known.</summary>
    [Fact]
    public void The_full_set_holds_every_privilege_and_a_bit_beyond_them_is_not_known()
    {
        Assert.Equal(DatabasePrivilegeSets.All, DatabasePrivilegeSets.MembersOf(DatabasePrivilegeSets.Full));
        Assert.True(DatabasePrivilegeSets.IsKnown(DatabasePrivilegeSets.Full));

        // The inverse control: the check refuses something, so a gate that accepted everything is caught.
        Assert.False(DatabasePrivilegeSets.IsKnown((DatabasePrivilegeSet)(1 << 18)));
    }
}
