using Maran.Modules.Identity.Commands.ResendSubUserInvitation;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Utilities.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Commands.ResendSubUserInvitation;

/// <summary>Behavioural contract of the handler that resends a sub-user's invitation.</summary>
public sealed class ResendSubUserInvitationCommandHandlerTests : IDisposable
{
    private readonly SubUserWorld _world = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _world.Dispose();
    }

    private ResendSubUserInvitationCommandHandler NewHandler(StubCustomerUser? caller = null)
    {
        var principal = caller ?? _world.Caller;
        return new ResendSubUserInvitationCommandHandler(
            _world.Context,
            _world.Guard(principal),
            new InvitationIssuer(_world.Context, _world.Accounts, new AccountOwnerFinder(_world.Context)),
            new InvitationMailComposer(new StubEmailTemplates(), new OptionsWrapper<PanelOptions>(new PanelOptions())),
            _world.Accounts,
            _world.Bus,
            new IdentityAuditJournal(_world.Audit, principal),
            _world.Clock,
            principal);
    }

    private static ResendSubUserInvitationCommand Command(Guid userId)
    {
        return new ResendSubUserInvitationCommand(userId, "203.0.113.7", "agent");
    }

    /// <summary>Stores a sub-user of <paramref name="accountId"/> who has not yet accepted, holding one live token.</summary>
    private async Task<(User User, InvitationToken Token)> SeedPendingAsync(Guid? accountId = null, string username = "deploy")
    {
        var user = User.Invite(Guid.NewGuid(), username, username + "@example.com", SubUserWorld.Now);
        var token = new InvitationToken(
            Guid.NewGuid(), user.Id, InvitationTokenHasher.Hash(InvitationTokenHasher.Generate()), SubUserWorld.Now);
        _world.Context.Users.Add(user);
        _world.Context.AccountMemberships.Add(AccountMembership.Admit(
            Guid.NewGuid(), user.Id, accountId ?? SubUserWorld.AccountId, AccountFeature.Databases, SubUserWorld.Now));
        _world.Context.InvitationTokens.Add(token);
        await _world.Context.SaveChangesAsync();
        return (user, token);
    }

    /// <summary>Resending retires the outstanding token and stores exactly one new, usable one.</summary>
    [Fact]
    public async Task Resending_retires_the_old_token_and_mints_one_new_one()
    {
        var (user, old) = await SeedPendingAsync();

        var result = await NewHandler().HandleAsync(Command(user.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var tokens = await _world.Context.InvitationTokens.Where(candidate => candidate.UserId == user.Id).ToListAsync();
        Assert.Equal(2, tokens.Count);
        Assert.Single(tokens, candidate =>
        {
            return candidate.IsUsable(_world.Clock.UtcNow);
        });
        Assert.False(tokens.Single(candidate =>
        {
            return candidate.Id == old.Id;
        }).IsUsable(_world.Clock.UtcNow));
    }

    /// <summary>The mail is published once, to the address stored on the login, and carries the plaintext whose digest is stored.</summary>
    [Fact]
    public async Task Resending_mails_the_stored_address_once_with_the_token_whose_digest_is_stored()
    {
        var (user, old) = await SeedPendingAsync();

        await NewHandler().HandleAsync(Command(user.Id), CancellationToken.None);

        var mail = Assert.IsType<SendMailRequested>(Assert.Single(_world.Bus.Published));
        Assert.Equal("deploy@example.com", mail.Recipient);
        var mailed = mail.Body[(mail.Body.LastIndexOf(':') + 1)..];
        var fresh = await _world.Context.InvitationTokens.SingleAsync(
            candidate => candidate.UserId == user.Id && candidate.Id != old.Id);
        Assert.Equal(InvitationTokenHasher.Hash(mailed), fresh.TokenHash);
    }

    /// <summary>The journal names the owner as actor and the sub-user as subject, and never the token.</summary>
    [Fact]
    public async Task Resending_is_journalled_without_the_token()
    {
        var (user, _) = await SeedPendingAsync();

        await NewHandler().HandleAsync(Command(user.Id), CancellationToken.None);

        var entry = Assert.Single(_world.Audit.Written);
        Assert.Equal(IdentityAuditActions.SubUserInvitationResent, entry.Action);
        Assert.Equal("deploy", entry.Subject);
        Assert.Equal(_world.Owner.Id, entry.ActorUserId);
        Assert.True(entry.Succeeded);
        var mail = Assert.IsType<SendMailRequested>(Assert.Single(_world.Bus.Published));
        var mailed = mail.Body[(mail.Body.LastIndexOf(':') + 1)..];
        Assert.DoesNotContain(mailed, entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>A member who has already chosen a password is refused and nothing is minted or mailed.</summary>
    [Fact]
    public async Task Resending_to_an_active_member_is_refused_and_mints_nothing()
    {
        var member = await _world.SeedMemberAsync("helper", AccountFeature.Databases);

        var result = await NewHandler().HandleAsync(Command(member.Id), CancellationToken.None);

        Assert.Equal("SubUserNotPending", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(0, await _world.Context.InvitationTokens.CountAsync());
        Assert.Empty(_world.Bus.Published);
        Assert.False(Assert.Single(_world.Audit.Written).Succeeded);
    }

    /// <summary>A sub-user of another account is indistinguishable from one that does not exist, and is left untouched.</summary>
    [Fact]
    public async Task Resending_to_another_accounts_sub_user_is_not_found()
    {
        var otherAccount = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");
        var (foreign, old) = await SeedPendingAsync(otherAccount, "foreign");

        var result = await NewHandler().HandleAsync(Command(foreign.Id), CancellationToken.None);

        Assert.Equal("SubUserNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(_world.Bus.Published);
        Assert.True((await _world.Context.InvitationTokens.SingleAsync()).IsUsable(_world.Clock.UtcNow));
        Assert.Equal(old.Id, (await _world.Context.InvitationTokens.SingleAsync()).Id);
    }

    /// <summary>An id that names nobody answers not found, as a foreign one does.</summary>
    [Fact]
    public async Task Resending_to_an_unknown_id_is_not_found()
    {
        var result = await NewHandler().HandleAsync(Command(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("SubUserNotFound", result.Error!.Code);
        Assert.Empty(_world.Bus.Published);
    }

    /// <summary>The owner's own login is not a team member and cannot be resent to through this action.</summary>
    [Fact]
    public async Task Resending_to_the_owner_is_not_found()
    {
        var result = await NewHandler().HandleAsync(Command(_world.Owner.Id), CancellationToken.None);

        Assert.Equal("SubUserNotFound", result.Error!.Code);
        Assert.Empty(_world.Bus.Published);
    }

    /// <summary>A team member cannot resend invitations, even to a pending teammate.</summary>
    [Fact]
    public async Task A_member_cannot_resend()
    {
        var (pending, _) = await SeedPendingAsync();
        var member = await _world.SeedMemberAsync("helper", AccountFeature.All);
        var caller = new StubCustomerUser(member.Id, member.Username, SubUserWorld.AccountId);

        var result = await NewHandler(caller).HandleAsync(Command(pending.Id), CancellationToken.None);

        Assert.Equal("SubUsersOwnerOnly", result.Error!.Code);
        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
        Assert.Empty(_world.Bus.Published);
        Assert.Equal(1, await _world.Context.InvitationTokens.CountAsync());
    }

    /// <summary>A suspended account is offered no way in, so nothing is minted or mailed for it.</summary>
    [Fact]
    public async Task A_suspended_account_cannot_resend()
    {
        var (user, _) = await SeedPendingAsync();
        _world.Accounts.Add(SubUserWorld.AccountId, "acme", maxFtpUsers: 1, AccountState.Suspended);

        var result = await NewHandler().HandleAsync(Command(user.Id), CancellationToken.None);

        Assert.Equal("InvitationAccountSuspended", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(_world.Bus.Published);
        Assert.Equal(1, await _world.Context.InvitationTokens.CountAsync());
    }

    /// <summary>The validator refuses an empty id with the not-found code.</summary>
    [Fact]
    public void The_validator_refuses_an_empty_id()
    {
        var verdict = new ResendSubUserInvitationCommandValidator().Validate(Command(Guid.Empty));

        Assert.Equal("SubUserNotFound", Assert.Single(verdict.Errors).ErrorMessage);
    }
}
