using Maran.Modules.Identity.Commands.ResendResellerInvitation;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Utilities.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Commands.ResendResellerInvitation;

/// <summary>
/// Behavioural contract of <see cref="ResendResellerInvitationCommandHandler"/>: an administrator
/// can have a reseller's invitation mailed again, only while the login is still waiting for its first
/// password and the reseller is in service, and the audit entry names the administrator and no token.
/// </summary>
public sealed class ResendResellerInvitationCommandHandlerTests : IDisposable
{
    /// <summary>The reseller whose login is invited.</summary>
    private static readonly Guid ResellerId = Guid.Parse("cccccccc-0000-4000-8000-000000000001");

    /// <summary>The fixed instant the clock reads.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The module's context, in memory.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The bus the invitation mail is published on.</summary>
    private readonly RecordingMessageBus _bus = new();

    /// <summary>The window onto the Accounts module's resellers.</summary>
    private readonly StubResellerDirectory _resellers = new();

    /// <summary>The clock.</summary>
    private readonly FakeClock _clock = new(Now);

    /// <summary>The administrator asking.</summary>
    private readonly StubCurrentUser _admin = new(Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002"), "the-admin");

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Builds the handler the way the module composes it.</summary>
    private ResendResellerInvitationCommandHandler NewHandler()
    {
        return new ResendResellerInvitationCommandHandler(
            _bus,
            new IdentityAuditJournal(_audit, _admin),
            new InvitationMailComposer(new StubEmailTemplates(), new OptionsWrapper<PanelOptions>(new PanelOptions())),
            new ResellerInvitationIssuer(
                _context,
                _resellers,
                new InvitationIssuer(_context, new StubAccountDirectory(), new AccountOwnerFinder(_context))),
            _clock,
            _admin);
    }

    /// <summary>Builds the request for the reseller under test.</summary>
    private static ResendResellerInvitationCommand Command()
    {
        return new ResendResellerInvitationCommand(ResellerId, "203.0.113.7", "unit-tests");
    }

    /// <summary>Stores the reseller's invited login, with a token whose plaintext the test knows.</summary>
    /// <param name="state">Whether the reseller is in service.</param>
    private async Task<User> SeedInvitedResellerLoginAsync(ResellerState state = ResellerState.Active)
    {
        _resellers.Add(ResellerId, "reseller-one", state);
        var user = User.InviteReseller(Guid.NewGuid(), "reseller-one", "reseller@example.com", ResellerId, Now);
        _context.Users.Add(user);
        _context.InvitationTokens.Add(
            new InvitationToken(Guid.NewGuid(), user.Id, InvitationTokenHasher.Hash("the-first-token"), Now));
        await _context.SaveChangesAsync();
        return user;
    }

    /// <summary>A pending reseller login is mailed a new token and the first one is spent.</summary>
    [Fact]
    public async Task Resending_to_a_pending_reseller_login_mails_a_new_token_and_spends_the_first()
    {
        await SeedInvitedResellerLoginAsync();

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var tokens = await _context.InvitationTokens.ToListAsync();
        Assert.Equal(2, tokens.Count);
        Assert.Single(tokens, token =>
        {
            return token.UsedAt == null;
        });
        var mail = Assert.IsType<SendMailRequested>(Assert.Single(_bus.Published));
        Assert.Equal("reseller@example.com", mail.Recipient);
    }

    /// <summary>The audit entry names the administrator as the actor and the reseller's login as the subject.</summary>
    [Fact]
    public async Task A_resent_reseller_invitation_is_journalled_under_the_administrator()
    {
        var login = await SeedInvitedResellerLoginAsync();

        await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.ResellerInvited, entry.Action);
        Assert.True(entry.Succeeded);
        Assert.Equal(_admin.UserId, entry.ActorUserId);
        Assert.NotEqual(login.Id, entry.ActorUserId);
    }

    /// <summary>A reseller nobody has heard of is refused with a not-found.</summary>
    [Fact]
    public async Task Resending_for_an_unknown_reseller_is_refused()
    {
        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.Equal("InvitationResellerNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(_bus.Published);
        AssertRefusalJournalled();
    }

    /// <summary>A reseller whose login was never created gets it rebuilt, invited, mailed and audited.</summary>
    [Fact]
    public async Task Resending_for_a_reseller_with_no_login_rebuilds_the_login_and_invites_it()
    {
        _resellers.Add(ResellerId, "reseller-one", contactEmail: "contact@example.com");

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var login = Assert.Single(await _context.Users.ToListAsync());
        Assert.Equal(ResellerId, login.ResellerId);
        Assert.Equal("reseller-one", login.Username);
        Assert.Equal("contact@example.com", login.Email);
        Assert.Equal(UserLifecycle.Invited, login.Lifecycle);
        Assert.Empty(await _context.AccountMemberships.ToListAsync());
        Assert.Single(await _context.InvitationTokens.ToListAsync(), token =>
        {
            return token.UserId == login.Id && token.UsedAt == null;
        });
        var mail = Assert.IsType<SendMailRequested>(Assert.Single(_bus.Published));
        Assert.Equal("contact@example.com", mail.Recipient);
        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.ResellerInvited, entry.Action);
        Assert.Equal(_admin.UserId, entry.ActorUserId);
    }

    /// <summary>A suspended reseller with no login is refused, and no login is created for it.</summary>
    [Fact]
    public async Task Resending_for_a_suspended_reseller_with_no_login_creates_none()
    {
        _resellers.Add(ResellerId, "reseller-one", ResellerState.Suspended);

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.Equal("InvitationResellerSuspended", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(await _context.Users.ToListAsync());
        Assert.Empty(_bus.Published);
        AssertRefusalJournalled();
    }

    /// <summary>A login that already has a password is refused: a new token would be a password reset in disguise.</summary>
    [Fact]
    public async Task Resending_to_a_reseller_login_that_has_a_password_is_refused()
    {
        var login = await SeedInvitedResellerLoginAsync();
        login.Activate("hash");
        await _context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.Equal("InvitationLoginNotPending", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(_bus.Published);
        Assert.Single(await _context.InvitationTokens.ToListAsync());
        AssertRefusalJournalled();
    }

    /// <summary>A suspended reseller is offered no way in: refused, its token kept, nothing mailed.</summary>
    [Fact]
    public async Task Resending_to_a_suspended_reseller_is_refused_and_keeps_its_token()
    {
        await SeedInvitedResellerLoginAsync(ResellerState.Suspended);

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.Equal("InvitationResellerSuspended", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(_bus.Published);
        Assert.Null(Assert.Single(await _context.InvitationTokens.ToListAsync()).UsedAt);
        AssertRefusalJournalled();
    }

    /// <summary>A successful resend is journalled as a success, naming the login, with the first token's plaintext nowhere in it.</summary>
    [Fact]
    public async Task A_successful_resend_is_journalled_as_a_success_and_carries_no_token()
    {
        await SeedInvitedResellerLoginAsync();

        await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.True(entry.Succeeded);
        Assert.Equal("reseller-one", entry.Subject);
        Assert.DoesNotContain("the-first-token", entry.ToString());
        Assert.DoesNotContain("token", entry.Subject, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Asserts exactly one failed entry for the administrator, naming the id the caller sent.</summary>
    private void AssertRefusalJournalled()
    {
        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.ResellerInvited, entry.Action);
        Assert.False(entry.Succeeded);
        Assert.Equal(_admin.UserId, entry.ActorUserId);
        Assert.Equal(ResellerId.ToString("D"), entry.Subject);
    }
}
