using Maran.Modules.Databases.Domain.Entities;
using Maran.Modules.Databases.Domain.Enums;

namespace Maran.Modules.Databases.Tests.Domain.Entities;

/// <summary>The rule that a user's grant is one row per database and that an empty set is no row.</summary>
public sealed class DatabaseUserTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid FirstDatabase = Guid.Parse("11111111-0000-4000-8000-000000000001");
    private static readonly Guid SecondDatabase = Guid.Parse("22222222-0000-4000-8000-000000000002");

    /// <summary>A new user holds nothing.</summary>
    [Fact]
    public void A_new_user_holds_nothing()
    {
        Assert.Empty(NewUser().Grants);
    }

    /// <summary>Granting on a database adds one grant holding exactly that set.</summary>
    [Fact]
    public void Granting_on_a_database_adds_one_grant_holding_exactly_that_set()
    {
        var user = NewUser();

        user.ReplacePrivileges(FirstDatabase, DatabasePrivilegeSet.Select, Guid.NewGuid(), Now);

        var grant = Assert.Single(user.Grants);
        Assert.Equal(FirstDatabase, grant.DatabaseId);
        Assert.Equal(user.Id, grant.DatabaseUserId);
        Assert.Equal(DatabasePrivilegeSet.Select, grant.Privileges);
    }

    /// <summary>Replacing the set on a database that has a grant changes it and adds no second row.</summary>
    [Fact]
    public void Replacing_the_set_on_a_database_that_has_a_grant_changes_it_and_adds_no_second_row()
    {
        var user = NewUser();
        var grantId = Guid.NewGuid();
        user.ReplacePrivileges(FirstDatabase, DatabasePrivilegeSet.Select, grantId, Now);

        user.ReplacePrivileges(
            FirstDatabase, DatabasePrivilegeSet.Select | DatabasePrivilegeSet.Update, Guid.NewGuid(), Now.AddHours(1));

        var grant = Assert.Single(user.Grants);
        Assert.Equal(grantId, grant.Id);
        Assert.Equal(DatabasePrivilegeSet.Select | DatabasePrivilegeSet.Update, grant.Privileges);
        Assert.Equal(Now, grant.CreatedAt);
    }

    /// <summary>An empty set removes the grant on that database and leaves the others.</summary>
    [Fact]
    public void An_empty_set_removes_the_grant_on_that_database_and_leaves_the_others()
    {
        var user = NewUser();
        user.ReplacePrivileges(FirstDatabase, DatabasePrivilegeSet.Select, Guid.NewGuid(), Now);
        user.ReplacePrivileges(SecondDatabase, DatabasePrivilegeSet.Insert, Guid.NewGuid(), Now);

        user.ReplacePrivileges(FirstDatabase, DatabasePrivilegeSet.None, Guid.NewGuid(), Now);

        var grant = Assert.Single(user.Grants);
        Assert.Equal(SecondDatabase, grant.DatabaseId);
    }

    /// <summary>An empty set on a database with no grant writes nothing.</summary>
    [Fact]
    public void An_empty_set_on_a_database_with_no_grant_writes_nothing()
    {
        var user = NewUser();

        user.ReplacePrivileges(FirstDatabase, DatabasePrivilegeSet.None, Guid.NewGuid(), Now);

        Assert.Empty(user.Grants);
    }

    /// <summary>Builds a user with no grants.</summary>
    private static DatabaseUser NewUser()
    {
        return new DatabaseUser(Guid.NewGuid(), Guid.NewGuid(), "reader", "acct_reader", Now);
    }
}
