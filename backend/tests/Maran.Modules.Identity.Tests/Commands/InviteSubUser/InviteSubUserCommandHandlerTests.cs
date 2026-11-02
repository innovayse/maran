using Maran.Modules.Identity.Commands.InviteSubUser;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Utilities.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Commands.InviteSubUser;

/// <summary>Behavioural contract of the sub-user invitation handler.</summary>
public sealed class InviteSubUserCommandHandlerTests : IDisposable
{
    private readonly SubUserWorld _world = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _world.Dispose();
    }

    private InviteSubUserCommandHandler NewHandler(StubCustomerUser? caller = null)
    {
        var principal = caller ?? _world.Caller;
        return new InviteSubUserCommandHandler(
            _world.Context,
            _world.Guard(principal),
            new InvitationIssuer(_world.Context, _world.Accounts),
            new InvitationMailComposer(new StubEmailTemplates(), new OptionsWrapper<PanelOptions>(new PanelOptions())),
            _world.Accounts,
            _world.Bus,
            new IdentityAuditJournal(_world.Audit, principal),
            _world.Clock,
            principal);
    }

    private static InviteSubUserCommand Command(
        string username = "deploy",
        string email = "deploy@example.com",
        params AccountFeature[] features)
    {
        return new InviteSubUserCommand(username, email, features, "203.0.113.7", "agent");
    }

    /// <summary>Inviting creates an invited login with a member membership holding the chosen features.</summary>
    [Fact]
    public async Task Inviting_creates_an_invited_login_with_the_chosen_features()
    {
        var result = await NewHandler().HandleAsync(
            Command(features: [AccountFeature.Databases, AccountFeature.Cron]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserLifecycle.Invited, result.Value.State);
        Assert.Equal([AccountFeature.Databases, AccountFeature.Cron], result.Value.Features);
        var user = await _world.Context.Users.SingleAsync(candidate => candidate.Username == "deploy");
        Assert.Null(user.AccountId);
        var membership = await _world.Context.AccountMemberships.SingleAsync(candidate => candidate.UserId == user.Id);
        Assert.Equal(MembershipRole.Member, membership.Role);
        Assert.Equal(SubUserWorld.AccountId, membership.AccountId);
        Assert.Equal(AccountFeature.Databases | AccountFeature.Cron, membership.Grants);
    }

    /// <summary>The invitation is the existing one-time token, mailed to the new person and stored as a digest.</summary>
    [Fact]
    public async Task Inviting_mails_one_invitation_and_stores_only_its_digest()
    {
        await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var mail = Assert.IsType<SendMailRequested>(Assert.Single(_world.Bus.Published));
        Assert.Equal("deploy@example.com", mail.Recipient);
        var user = await _world.Context.Users.SingleAsync(candidate => candidate.Username == "deploy");
        var token = await _world.Context.InvitationTokens.SingleAsync(candidate => candidate.UserId == user.Id);
        Assert.True(token.IsUsable(_world.Clock.UtcNow));

        // The stub templates render "InvitationBody:InvitationTokenOnly:<token>", so what follows the
        // last colon is the plaintext the person receives; its digest must be the one stored.
        var mailed = mail.Body[(mail.Body.LastIndexOf(':') + 1)..];
        Assert.Equal(InvitationTokenHasher.Hash(mailed), token.TokenHash);
        Assert.NotEqual(mailed, token.TokenHash);
    }

    /// <summary>The invitation is journalled with the owner as actor and the new login as subject, and never the token.</summary>
    [Fact]
    public async Task Inviting_is_journalled_without_the_token()
    {
        await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var entry = Assert.Single(_world.Audit.Written);
        Assert.Equal(AuditActions.SubUserInvited, entry.Action);
        Assert.Equal("deploy", entry.Subject);
        Assert.Equal(_world.Owner.Id, entry.ActorUserId);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A team member cannot invite anybody.</summary>
    [Fact]
    public async Task A_member_cannot_invite()
    {
        var member = await _world.SeedMemberAsync("helper", AccountFeature.All);
        var caller = new StubCustomerUser(member.Id, member.Username, SubUserWorld.AccountId);

        var result = await NewHandler(caller).HandleAsync(Command(), CancellationToken.None);

        Assert.Equal("SubUsersOwnerOnly", result.Error!.Code);
        Assert.Empty(_world.Bus.Published);
        Assert.False(Assert.Single(_world.Audit.Written).Succeeded);
        Assert.False(await _world.Context.Users.AnyAsync(candidate => candidate.Username == "deploy"));
    }

    /// <summary>A login name already in use is refused and nothing is created or mailed.</summary>
    [Fact]
    public async Task A_taken_login_name_is_refused()
    {
        var result = await NewHandler().HandleAsync(Command(username: "acme"), CancellationToken.None);

        Assert.Equal("SubUserLoginTaken", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(_world.Bus.Published);
        Assert.Equal(1, await _world.Context.AccountMemberships.CountAsync());
    }

    /// <summary>An address already in use is refused: an existing person is not joined to the team unasked.</summary>
    [Fact]
    public async Task A_taken_email_address_is_refused()
    {
        var result = await NewHandler().HandleAsync(Command(email: "owner@example.com"), CancellationToken.None);

        Assert.Equal("SubUserEmailTaken", result.Error!.Code);
        Assert.Empty(_world.Bus.Published);
    }

    /// <summary>A suspended account is offered no way in for anybody, sub-users included.</summary>
    [Fact]
    public async Task A_suspended_account_cannot_invite()
    {
        _world.Accounts.Add(SubUserWorld.AccountId, "acme", maxFtpUsers: 1, AccountState.Suspended);

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.Equal("InvitationAccountSuspended", result.Error!.Code);
        Assert.Empty(_world.Bus.Published);
    }
}
