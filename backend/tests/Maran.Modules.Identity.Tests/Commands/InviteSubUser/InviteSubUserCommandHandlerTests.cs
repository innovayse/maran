using Maran.Modules.Identity.Commands.InviteSubUser;
using Maran.Modules.Identity.Common;
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

    private InviteSubUserCommandHandler NewHandler(StubCustomerUser? caller = null, int maxPerAccount = 25)
    {
        var principal = caller ?? _world.Caller;
        return new InviteSubUserCommandHandler(
            _world.Context,
            _world.Guard(principal),
            _world.TeamLock,
            new InvitationIssuer(_world.Context, _world.Accounts, new AccountOwnerFinder(_world.Context)),
            new InvitationMailComposer(new StubEmailTemplates(), new OptionsWrapper<PanelOptions>(new PanelOptions())),
            _world.Accounts,
            _world.Bus,
            new IdentityAuditJournal(_world.Audit, principal),
            _world.Clock,
            principal,
            new OptionsWrapper<SubUserOptions>(new SubUserOptions { MaxPerAccount = maxPerAccount }));
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
        Assert.Equal(new SubUserInvitationDto("deploy", "deploy@example.com"), result.Value);
        var user = await _world.Context.Users.SingleAsync(candidate => candidate.Username == "deploy");
        Assert.Equal(UserLifecycle.Invited, user.Lifecycle);
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

    /// <summary>
    /// A login name already in use answers exactly as a free one does and creates and mails nothing,
    /// so the answer cannot be used to test which names exist on the server.
    /// </summary>
    [Fact]
    public async Task A_taken_login_name_is_answered_like_a_free_one_and_creates_nothing()
    {
        var taken = await NewHandler().HandleAsync(Command(username: "acme"), CancellationToken.None);

        Assert.True(taken.IsSuccess);
        Assert.Equal(new SubUserInvitationDto("acme", "deploy@example.com"), taken.Value);
        Assert.Empty(_world.Bus.Published);
        Assert.Equal(1, await _world.Context.AccountMemberships.CountAsync());
        Assert.Equal(1, await _world.Context.Users.CountAsync());
    }

    /// <summary>An address already in use is answered as a free one and the existing person is neither joined nor mailed.</summary>
    [Fact]
    public async Task A_taken_email_address_is_answered_like_a_free_one_and_mails_nobody()
    {
        var taken = await NewHandler().HandleAsync(Command(email: "owner@example.com"), CancellationToken.None);

        Assert.True(taken.IsSuccess);
        Assert.Equal(new SubUserInvitationDto("deploy", "owner@example.com"), taken.Value);
        Assert.Empty(_world.Bus.Published);
        Assert.Equal(1, await _world.Context.AccountMemberships.CountAsync());
    }

    /// <summary>The taken request and the free one differ in nothing the caller can read: both are successes of one shape.</summary>
    [Fact]
    public async Task A_taken_and_a_free_request_return_the_same_shape_of_answer()
    {
        var free = await NewHandler().HandleAsync(Command(username: "fresh", email: "fresh@example.com"), CancellationToken.None);
        var taken = await NewHandler().HandleAsync(Command(username: "acme", email: "other@example.com"), CancellationToken.None);

        Assert.True(free.IsSuccess);
        Assert.True(taken.IsSuccess);
        Assert.Equal(free.Value.GetType(), taken.Value.GetType());
    }

    /// <summary>
    /// A taken request does the work of a free one in the same shape: it stages a login, a membership
    /// and a token and renders the mail, then undoes it, while a free request commits the same three.
    /// </summary>
    [Fact]
    public async Task A_taken_request_stages_the_same_rows_as_a_free_one_and_rolls_them_back()
    {
        var free = new SubUserWorld();
        await new InviteSubUserCommandHandler(
            free.Context, free.Guard(), free.TeamLock,
            new InvitationIssuer(free.Context, free.Accounts, new AccountOwnerFinder(free.Context)),
            new InvitationMailComposer(new StubEmailTemplates(), new OptionsWrapper<PanelOptions>(new PanelOptions())),
            free.Accounts, free.Bus, new IdentityAuditJournal(free.Audit, free.Caller), free.Clock, free.Caller,
            new OptionsWrapper<SubUserOptions>(new SubUserOptions()))
            .HandleAsync(Command(username: "fresh", email: "fresh@example.com"), CancellationToken.None);
        await NewHandler().HandleAsync(Command(username: "acme"), CancellationToken.None);

        Assert.Equal((1, 0), (free.TeamLock.Commits, free.TeamLock.Rollbacks));
        Assert.Equal((0, 1), (_world.TeamLock.Commits, _world.TeamLock.Rollbacks));
        Assert.Equal(3, _world.TeamLock.RowsRolledBack);
        free.Dispose();
    }

    /// <summary>A taken request leaves no decoy behind and mails nobody, whichever column matched.</summary>
    [Fact]
    public async Task A_taken_request_leaves_no_decoy_login_behind()
    {
        await NewHandler().HandleAsync(Command(email: "owner@example.com"), CancellationToken.None);

        Assert.Equal(1, await _world.Context.Users.CountAsync());
        Assert.Equal(1, await _world.Context.AccountMemberships.CountAsync());
        Assert.Equal(0, await _world.Context.InvitationTokens.CountAsync());
        Assert.Empty(_world.Bus.Published);
    }

    /// <summary>The real outcome of a taken request is journalled as unsuccessful, where only the administrator reads it.</summary>
    [Fact]
    public async Task A_taken_request_is_journalled_as_unsuccessful()
    {
        await NewHandler().HandleAsync(Command(username: "acme"), CancellationToken.None);

        var entry = Assert.Single(_world.Audit.Written);
        Assert.Equal(AuditActions.SubUserInvited, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>An account holding its cap of sub-users is refused with its own code, and nothing is created or mailed.</summary>
    [Fact]
    public async Task An_account_at_its_cap_is_refused_with_the_limit_code()
    {
        await _world.SeedMemberAsync("one", AccountFeature.All);

        var result = await NewHandler(maxPerAccount: 1).HandleAsync(Command(), CancellationToken.None);

        Assert.Equal("SubUserLimitReached", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(_world.Bus.Published);
        Assert.False(await _world.Context.Users.AnyAsync(candidate => candidate.Username == "deploy"));
    }

    /// <summary>The cap is checked before the taken test, so a full account is told so whatever name it asked for.</summary>
    [Fact]
    public async Task A_full_account_is_refused_even_for_a_taken_name()
    {
        await _world.SeedMemberAsync("one", AccountFeature.All);

        var result = await NewHandler(maxPerAccount: 1).HandleAsync(Command(username: "acme"), CancellationToken.None);

        Assert.Equal("SubUserLimitReached", result.Error!.Code);
    }

    /// <summary>A pending invitation counts against the cap, as an accepted member does.</summary>
    [Fact]
    public async Task A_pending_invitation_counts_against_the_cap()
    {
        await NewHandler(maxPerAccount: 1).HandleAsync(Command(), CancellationToken.None);

        var second = await NewHandler(maxPerAccount: 1).HandleAsync(
            Command(username: "second", email: "second@example.com"), CancellationToken.None);

        Assert.Equal("SubUserLimitReached", second.Error!.Code);
    }

    /// <summary>An account one short of its cap can still invite (the inverse control for the refusals above).</summary>
    [Fact]
    public async Task An_account_below_its_cap_can_invite()
    {
        await _world.SeedMemberAsync("one", AccountFeature.All);

        var result = await NewHandler(maxPerAccount: 2).HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(_world.Bus.Published);
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
