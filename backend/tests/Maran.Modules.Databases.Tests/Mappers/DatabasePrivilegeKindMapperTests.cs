using Maran.Agent.Client.Services.DbService;
using Maran.Modules.Databases.Domain.Enums;
using Maran.Modules.Databases.Mappers;

namespace Maran.Modules.Databases.Tests.Mappers;

/// <summary>The panel's privilege list and the agent client's stay the same eighteen.</summary>
public sealed class DatabasePrivilegeKindMapperTests
{
    /// <summary>Every privilege maps to the agent client's member of the same name.</summary>
    [Fact]
    public void Every_privilege_maps_to_the_agent_clients_member_of_the_same_name()
    {
        foreach (var privilege in Enum.GetValues<DatabasePrivilege>())
        {
            var kind = DatabasePrivilegeKindMapper.ToKind(privilege);

            Assert.Equal(privilege.ToString(), kind.ToString());
        }
    }

    /// <summary>The two lists have the same number of members, so neither side has one the other lacks.</summary>
    [Fact]
    public void The_two_lists_have_the_same_number_of_members()
    {
        Assert.Equal(Enum.GetValues<DatabasePrivilege>().Length, Enum.GetValues<DatabasePrivilegeKind>().Length);
    }

    /// <summary>A value outside the list is refused rather than mapped to something.</summary>
    [Fact]
    public void A_value_outside_the_list_is_refused_rather_than_mapped_to_something()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => { return DatabasePrivilegeKindMapper.ToKind((DatabasePrivilege)99); });
    }
}
