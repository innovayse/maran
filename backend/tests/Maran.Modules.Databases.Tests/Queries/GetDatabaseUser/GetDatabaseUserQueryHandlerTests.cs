using Maran.Modules.Databases.Queries.GetDatabaseUser;
using Maran.Modules.Databases.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Databases.Tests.Queries.GetDatabaseUser;

/// <summary>What reading one user shows, and that another tenants user is not there.</summary>
public sealed class GetDatabaseUserQueryHandlerTests
{
    /// <summary>A customer reads their own user with its grants.</summary>
    [Fact]
    public async Task A_customer_reads_their_own_user_with_its_grants()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await new GetDatabaseUserQueryHandler(world.Context, world.Accounts)
            .HandleAsync(new GetDatabaseUserQuery(world.DedicatedUserId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("alice_shopuser", result.Value.FullName);
        Assert.True(result.Value.IsDedicated);
        Assert.Equal("alice_shop", Assert.Single(result.Value.Grants).DatabaseFullName);
        Assert.Empty(result.Value.GrantableDatabases);
    }

    /// <summary>A user holding nothing is offered every database of its account to grant on.</summary>
    [Fact]
    public async Task A_user_holding_nothing_is_offered_every_database_of_its_account_to_grant_on()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await new GetDatabaseUserQueryHandler(world.Context, world.Accounts)
            .HandleAsync(new GetDatabaseUserQuery(world.ReaderUserId), CancellationToken.None);

        var offered = Assert.Single(result.Value.GrantableDatabases);
        Assert.Equal(world.OwnDatabaseId, offered.Id);
        Assert.Equal("alice_shop", offered.FullName);
        Assert.False(result.Value.IsDedicated);
    }

    /// <summary>The databases offered never include another tenants.</summary>
    [Fact]
    public async Task The_databases_offered_never_include_another_tenants()
    {
        var world = await UsersTestWorld.CreateAsync(asAdministrator: true);

        var result = await new GetDatabaseUserQueryHandler(world.Context, world.Accounts)
            .HandleAsync(new GetDatabaseUserQuery(world.ReaderUserId), CancellationToken.None);

        Assert.DoesNotContain(result.Value.GrantableDatabases, offered => { return offered.Id == world.StrangerDatabaseId; });
    }

    /// <summary>Another tenants user answers not found.</summary>
    [Fact]
    public async Task Another_tenants_user_answers_not_found()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await new GetDatabaseUserQueryHandler(world.Context, world.Accounts)
            .HandleAsync(new GetDatabaseUserQuery(world.StrangerUserId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("DatabaseUserNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }
}
