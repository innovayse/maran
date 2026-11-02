using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>Behavioural contract of <c>SubUserOwnerGuard</c>: who may manage an account's team.</summary>
public sealed class SubUserOwnerGuardTests : IDisposable
{
    private readonly SubUserWorld _world = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _world.Dispose();
    }

    /// <summary>The owner is told which account they own.</summary>
    [Fact]
    public async Task The_owner_is_given_the_account_they_are_acting_for()
    {
        var result = await _world.Guard().RequireOwnedAccountAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubUserWorld.AccountId, result.Value);
    }

    /// <summary>A team member cannot manage the team.</summary>
    [Fact]
    public async Task A_member_is_refused()
    {
        var member = await _world.SeedMemberAsync("deploy", AccountFeature.All);
        var caller = new StubCustomerUser(member.Id, member.Username, SubUserWorld.AccountId);

        var result = await _world.Guard(caller).RequireOwnedAccountAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SubUsersOwnerOnly", result.Error!.Code);
        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
    }

    /// <summary>A caller acting for no account (an administrator) has none to manage.</summary>
    [Fact]
    public async Task A_caller_with_no_account_is_refused()
    {
        var caller = new StubCustomerUser(Guid.NewGuid(), "admin", accountId: null);

        var result = await _world.Guard(caller).RequireOwnedAccountAsync(CancellationToken.None);

        Assert.Equal("SubUsersOwnerOnly", result.Error!.Code);
    }

    /// <summary>The owner of another account is not an owner of this one.</summary>
    [Fact]
    public async Task An_owner_acting_for_an_account_they_do_not_own_is_refused()
    {
        var caller = new StubCustomerUser(_world.Owner.Id, _world.Owner.Username, Guid.NewGuid());

        var result = await _world.Guard(caller).RequireOwnedAccountAsync(CancellationToken.None);

        Assert.Equal("SubUsersOwnerOnly", result.Error!.Code);
    }
}
