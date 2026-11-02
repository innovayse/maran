using Maran.Modules.Identity.Commands.ChangeSubUserGrants;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Commands.ChangeSubUserGrants;

/// <summary>Behavioural contract of the sub-user grants handler.</summary>
public sealed class ChangeSubUserGrantsCommandHandlerTests : IDisposable
{
    private readonly SubUserWorld _world = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _world.Dispose();
    }

    private ChangeSubUserGrantsCommandHandler NewHandler(StubCustomerUser? caller = null)
    {
        var principal = caller ?? _world.Caller;
        return new ChangeSubUserGrantsCommandHandler(
            _world.Context, _world.Guard(principal), new IdentityAuditJournal(_world.Audit, principal), principal);
    }

    private static ChangeSubUserGrantsCommand Command(Guid userId, params AccountFeature[] features)
    {
        return new ChangeSubUserGrantsCommand(features, userId, "203.0.113.7", "agent");
    }

    /// <summary>The sub-user's grants are replaced as a whole, and the answer reports the new set.</summary>
    [Fact]
    public async Task Changing_grants_replaces_the_set()
    {
        var member = await _world.SeedMemberAsync("deploy", AccountFeature.Databases | AccountFeature.Cron);

        var result = await NewHandler().HandleAsync(Command(member.Id, AccountFeature.Backups), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([AccountFeature.Backups], result.Value.Features);
        var stored = await _world.Context.AccountMemberships.SingleAsync(candidate => candidate.UserId == member.Id);
        Assert.Equal(AccountFeature.Backups, stored.Grants);
    }

    /// <summary>The change is journalled against the sub-user's login name.</summary>
    [Fact]
    public async Task Changing_grants_is_journalled()
    {
        var member = await _world.SeedMemberAsync("deploy", AccountFeature.Databases);

        await NewHandler().HandleAsync(Command(member.Id), CancellationToken.None);

        var entry = Assert.Single(_world.Audit.Written);
        Assert.Equal(AuditActions.SubUserGrantsChanged, entry.Action);
        Assert.Equal("deploy", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A sub-user of another account is not found, exactly as an unknown id is.</summary>
    [Fact]
    public async Task Another_accounts_sub_user_is_not_found_and_is_left_unchanged()
    {
        var stranger = await _world.SeedMemberAsync("rival", AccountFeature.Databases, accountId: Guid.NewGuid());

        var result = await NewHandler().HandleAsync(Command(stranger.Id, AccountFeature.Backups), CancellationToken.None);

        Assert.Equal("SubUserNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        var stored = await _world.Context.AccountMemberships.SingleAsync(candidate => candidate.UserId == stranger.Id);
        Assert.Equal(AccountFeature.Databases, stored.Grants);
        Assert.False(Assert.Single(_world.Audit.Written).Succeeded);
    }

    /// <summary>The owner cannot be addressed as a sub-user of their own account.</summary>
    [Fact]
    public async Task The_owner_is_not_a_sub_user()
    {
        var result = await NewHandler().HandleAsync(Command(_world.Owner.Id), CancellationToken.None);

        Assert.Equal("SubUserNotFound", result.Error!.Code);
    }

    /// <summary>A team member cannot change anybody's grants, their own included.</summary>
    [Fact]
    public async Task A_member_cannot_widen_their_own_grants()
    {
        var member = await _world.SeedMemberAsync("deploy", AccountFeature.Databases);
        var caller = new StubCustomerUser(member.Id, member.Username, SubUserWorld.AccountId);

        var result = await NewHandler(caller).HandleAsync(Command(member.Id, AccountFeature.All), CancellationToken.None);

        Assert.Equal("SubUsersOwnerOnly", result.Error!.Code);
        var stored = await _world.Context.AccountMemberships.SingleAsync(candidate => candidate.UserId == member.Id);
        Assert.Equal(AccountFeature.Databases, stored.Grants);
    }
}
