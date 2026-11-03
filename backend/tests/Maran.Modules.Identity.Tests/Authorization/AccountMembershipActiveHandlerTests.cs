using System.Security.Claims;
using Maran.Modules.Identity.Authorization;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Maran.Modules.Identity.Tests.Authorization;

/// <summary>
/// Behavioural contract of the per-request membership check: a customer token is honoured only while its
/// login still holds an active membership of the account it names (issues #326, #331, #340).
/// </summary>
public sealed class AccountMembershipActiveHandlerTests : IDisposable
{
    private readonly SubUserWorld _world = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _world.Dispose();
    }

    private static ClaimsPrincipal Customer(Guid userId, Guid accountId)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(PanelClaimTypes.Role, "Customer"),
                new Claim(PanelClaimTypes.UserId, userId.ToString()),
                new Claim(PanelClaimTypes.AccountId, accountId.ToString()),
            ],
            "Bearer"));
    }

    private async Task<AuthorizationHandlerContext> EvaluateAsync(ClaimsPrincipal user)
    {
        var requirement = new AccountMembershipActiveRequirement();
        var context = new AuthorizationHandlerContext([requirement], user, null);
        await new AccountMembershipActiveHandler(_world.Context).HandleAsync(context);
        return context;
    }

    /// <summary>The owner's token is honoured while the membership is active.</summary>
    [Fact]
    public async Task An_active_owner_token_is_honoured()
    {
        var context = await EvaluateAsync(Customer(_world.Owner.Id, SubUserWorld.AccountId));

        Assert.True(context.HasSucceeded);
    }

    /// <summary>A member's token is honoured while the membership is active, whatever their grants.</summary>
    [Fact]
    public async Task An_active_members_token_is_honoured()
    {
        var member = await _world.SeedMemberAsync("deploy", AccountFeature.None);

        var context = await EvaluateAsync(Customer(member.Id, SubUserWorld.AccountId));

        Assert.True(context.HasSucceeded);
    }

    /// <summary>A token whose login holds no membership of the account is refused: the member was removed while it was live.</summary>
    [Fact]
    public async Task A_token_whose_login_holds_no_membership_is_refused()
    {
        var context = await EvaluateAsync(Customer(Guid.NewGuid(), SubUserWorld.AccountId));

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
    }

    /// <summary>A member removed after their token was issued is refused on the very next evaluation.</summary>
    [Fact]
    public async Task A_member_removed_after_the_token_was_issued_is_refused_at_once()
    {
        var member = await _world.SeedMemberAsync("deploy", AccountFeature.All);
        var token = Customer(member.Id, SubUserWorld.AccountId);
        Assert.True((await EvaluateAsync(token)).HasSucceeded);

        _world.Context.AccountMemberships.Remove(
            _world.Context.AccountMemberships.Single(row => row.UserId == member.Id));
        await _world.Context.SaveChangesAsync();

        Assert.False((await EvaluateAsync(token)).HasSucceeded);
    }

    /// <summary>A membership of another account says nothing about this one.</summary>
    [Fact]
    public async Task A_membership_of_another_account_does_not_honour_the_token()
    {
        var member = await _world.SeedMemberAsync("deploy", AccountFeature.All, Guid.NewGuid());

        var context = await EvaluateAsync(Customer(member.Id, SubUserWorld.AccountId));

        Assert.False(context.HasSucceeded);
    }

    /// <summary>A suspended membership leaves the requirement unmet, not failed, so the host can answer with the suspension code.</summary>
    [Fact]
    public async Task A_suspended_membership_leaves_the_requirement_unmet()
    {
        _world.Context.AccountMemberships.Single(row => row.UserId == _world.Owner.Id).Suspend();
        await _world.Context.SaveChangesAsync();

        var context = await EvaluateAsync(Customer(_world.Owner.Id, SubUserWorld.AccountId));

        Assert.False(context.HasSucceeded);
        Assert.False(context.HasFailed);
        Assert.Single(context.PendingRequirements);
    }

    /// <summary>A token naming no account and a token that is not a customer's are let through: there is no membership to ask about.</summary>
    [Fact]
    public async Task A_token_that_speaks_for_no_account_is_let_through()
    {
        var accountless = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(PanelClaimTypes.Role, "Customer"),
                new Claim(PanelClaimTypes.UserId, Guid.NewGuid().ToString()),
            ],
            "Bearer"));
        var administrator = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(PanelClaimTypes.Role, "Admin"), new Claim(PanelClaimTypes.UserId, Guid.NewGuid().ToString())],
            "Bearer"));

        Assert.True((await EvaluateAsync(accountless)).HasSucceeded);
        Assert.True((await EvaluateAsync(administrator)).HasSucceeded);
    }
}
