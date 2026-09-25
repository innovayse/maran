using Maran.Modules.Identity.Commands.ResendInvitation;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Commands.ResendInvitation;

/// <summary>Behavioural contract of the invitation-resend handler.</summary>
public sealed class ResendInvitationCommandHandlerTests : IDisposable
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly RecordingAuditWriter _audit = new();
    private readonly RecordingMessageBus _bus = new();
    private readonly StubAccountDirectory _accountDirectory = new();
    private readonly FakeClock _clock = new(Now);
    private readonly StubCurrentUser _currentUser = new(Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002"), "the-admin");

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private ResendInvitationCommandHandler NewHandler()
    {
        return new ResendInvitationCommandHandler(
            _context,
            _bus,
            new IdentityAuditJournal(_audit, _currentUser),
            new InvitationMailComposer(
                new StubEmailTemplates(), new OptionsWrapper<PanelOptions>(new PanelOptions())),
            _accountDirectory,
            _clock,
            _currentUser);
    }

    private static ResendInvitationCommand Command()
    {
        return new ResendInvitationCommand(AccountId, "203.0.113.7", "agent");
    }

    private async Task<User> SeedInvitedUserAsync()
    {
        var user = User.Invite(Guid.NewGuid(), "owner-one", "owner@example.com", AccountId, Now);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    /// <summary>Resend on a missing login creates it, from the account's own directory entry.</summary>
    [Fact]
    public async Task Resend_on_a_missing_login_creates_it()
    {
        _accountDirectory.Add(AccountId, "owner-one", maxFtpUsers: 1);

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var user = Assert.Single(await _context.Users.Where(candidate => candidate.AccountId == AccountId).ToListAsync());
        Assert.Equal(UserState.Invited, user.State);
        Assert.Single(await _context.InvitationTokens.ToListAsync());
        Assert.Single(_bus.Published);
    }

    /// <summary>Resend for an account nobody has heard of is refused.</summary>
    [Fact]
    public async Task Resend_for_an_unknown_account_is_refused()
    {
        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("InvitationAccountNotFound", result.Error!.Code);
    }

    /// <summary>Resend on an already active login is refused.</summary>
    /// <remarks>
    /// A fresh token for a live account is a password reset in disguise that bypasses
    /// <c>reset-password</c>'s own rate limit — the reason the handler's own remarks give for
    /// refusing anything other than <c>Invited</c>.
    /// </remarks>
    [Fact]
    public async Task Resend_on_an_already_active_login_is_refused()
    {
        var user = await SeedInvitedUserAsync();
        user.Activate("hash");
        await _context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("InvitationLoginNotPending", result.Error!.Code);
        Assert.Empty(_bus.Published);
    }

    /// <summary>Resend on a suspended login is refused, the same way an active one is.</summary>
    /// <remarks>
    /// A suspended login was active before it was suspended and already has a real password;
    /// <c>User.Activate</c> would overwrite it and unconditionally lift the suspension.
    /// </remarks>
    [Fact]
    public async Task Resend_on_a_suspended_login_is_refused()
    {
        var user = await SeedInvitedUserAsync();
        user.Activate("hash");
        user.Suspend();
        await _context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("InvitationLoginNotPending", result.Error!.Code);
    }

    /// <summary>Resend retires the previous outstanding token.</summary>
    [Fact]
    public async Task Resend_retires_the_previous_outstanding_token()
    {
        await SeedInvitedUserAsync();
        await NewHandler().HandleAsync(Command(), CancellationToken.None);
        var first = await _context.InvitationTokens.SingleAsync();

        _clock.Advance(TimeSpan.FromMinutes(1));
        await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var stored = await _context.InvitationTokens.Where(token => token.UserId == first.UserId).ToListAsync();
        Assert.Equal(2, stored.Count);
        var previous = Assert.Single(stored, token =>
        {
            return token.Id == first.Id;
        });
        Assert.NotNull(previous.UsedAt);
        var latest = Assert.Single(stored, token =>
        {
            return token.Id != first.Id;
        });
        Assert.Null(latest.UsedAt);
    }

    /// <summary>A resent invitation is journalled without the token.</summary>
    [Fact]
    public async Task A_resent_invitation_is_journalled_without_the_token()
    {
        await SeedInvitedUserAsync();

        await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(AuditActions.CustomerInvited, entry.Action);
        Assert.True(entry.Succeeded);
    }

    /// <summary>
    /// A resend's audit entry names the ADMINISTRATOR who asked for it as the actor, never the
    /// invited customer the resend is about.
    /// </summary>
    /// <remarks>
    /// Before this was fixed, the entry's <c>ActorUserId</c> was the invited user's own id and its
    /// <c>ActorUsername</c> was blank — the shape of "somebody verified themselves as this customer",
    /// which never happened: the customer never signed in, an administrator asked the panel to
    /// re-mail them a link.
    /// </remarks>
    [Fact]
    public async Task A_resent_invitations_audit_entry_names_the_administrator_not_the_customer()
    {
        var invited = await SeedInvitedUserAsync();

        await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(_currentUser.UserId, entry.ActorUserId);
        Assert.Equal(_currentUser.Username, entry.ActorUsername);
        Assert.NotEqual(invited.Id, entry.ActorUserId);
    }
}
