using Maran.Modules.Databases.IntegrationEvents.Handlers;
using Maran.Modules.Databases.Tests.TestSupport;
using Maran.Sdk.Events;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Databases.Tests.IntegrationEvents.Handlers;

/// <summary>That deleting an account releases its database users and their grants too.</summary>
public sealed class AccountDeletingUsersTests
{
    /// <summary>Deleting an account removes every user row it owns and keeps another tenants.</summary>
    [Fact]
    public async Task Deleting_an_account_removes_every_user_row_it_owns_and_keeps_another_tenants()
    {
        var world = await UsersTestWorld.CreateAsync();
        using var context = DatabasesTestContext.Create(FakeCurrentUser.Admin(), world.Store);

        await new AccountDeletingHandler(context)
            .HandleAsync(new AccountDeleting(UsersTestWorld.OwnerAccountId, "alice"), CancellationToken.None);

        var remaining = await world.AllUsersAsync();
        Assert.Equal([world.StrangerUserId], remaining.Select(user => { return user.Id; }));
        Assert.Single(Assert.Single(remaining).Grants);
    }

    /// <summary>An account owning only users, with no database, is still released.</summary>
    [Fact]
    public async Task An_account_owning_only_users_with_no_database_is_still_released()
    {
        var world = await UsersTestWorld.CreateAsync();
        using var admin = DatabasesTestContext.Create(FakeCurrentUser.Admin(), world.Store);
        admin.Databases.RemoveRange(admin.Databases.Where(database => database.Id == world.OwnDatabaseId));
        await admin.SaveChangesAsync();

        await new AccountDeletingHandler(admin)
            .HandleAsync(new AccountDeleting(UsersTestWorld.OwnerAccountId, "alice"), CancellationToken.None);

        Assert.DoesNotContain(await world.AllUsersAsync(), user => { return user.AccountId == UsersTestWorld.OwnerAccountId; });
    }
}
