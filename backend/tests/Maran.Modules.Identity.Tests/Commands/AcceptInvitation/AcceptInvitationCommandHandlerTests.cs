using Maran.Modules.Identity.Commands.AcceptInvitation;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Security;
using Maran.SharedKernel.Utilities.Tokens;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Commands.AcceptInvitation;

/// <summary>Behavioural contract of the invitation-acceptance handler.</summary>
public sealed class AcceptInvitationCommandHandlerTests : IAsyncLifetime
{
    private const string NewPassword = "correct horse battery staple";

    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly RecordingAuditWriter _audit = new();
    private readonly Argon2idPasswordHasher _hasher = new();
    private readonly FakeClock _clock = new(Now);
    private readonly Guid _userId = Guid.NewGuid();

    /// <summary>Seeds the invited login every test accepts an invitation for.</summary>
    public async Task InitializeAsync()
    {
        _context.Users.Add(User.Invite(_userId, "owner-one", "owner@example.com", Guid.NewGuid(), Now));
        await _context.SaveChangesAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    private AcceptInvitationCommandHandler NewHandler()
    {
        return new AcceptInvitationCommandHandler(
            _context, _hasher, new IdentityAuditJournal(_audit, new StubCurrentUser()), _clock);
    }

    private async Task<string> IssueTokenAsync()
    {
        var token = InvitationTokenHasher.Generate();
        _context.InvitationTokens.Add(
            new InvitationToken(Guid.NewGuid(), _userId, InvitationTokenHasher.Hash(token), _clock.UtcNow));
        await _context.SaveChangesAsync();
        return token;
    }

    private static AcceptInvitationCommand Command(string token)
    {
        return new AcceptInvitationCommand(token, NewPassword, "203.0.113.7", "agent");
    }

    /// <summary>A valid token sets the password and activates the login.</summary>
    [Fact]
    public async Task A_valid_token_sets_the_password_and_activates_the_login()
    {
        var token = await IssueTokenAsync();

        var result = await NewHandler().HandleAsync(Command(token), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var user = await _context.Users.SingleAsync(candidate => candidate.Id == _userId);
        Assert.Equal(UserState.Active, user.State);
        Assert.True(_hasher.Verify(NewPassword, user.PasswordHash));
    }

    /// <summary>
    /// The same token presented twice: the second attempt is refused, and the refusal is identical
    /// to the one an unknown token gets.
    /// </summary>
    /// <remarks>
    /// Equality is asserted on the whole <c>Error</c> — a record — rather than on a single
    /// field, so a handler that told the two apart on any axis (code, error type) fails this rather
    /// than only the axis a narrower assertion happened to check.
    /// </remarks>
    [Fact]
    public async Task The_same_token_presented_twice_is_refused_identically_to_an_unknown_token()
    {
        var token = await IssueTokenAsync();
        await NewHandler().HandleAsync(Command(token), CancellationToken.None);

        var replayed = await NewHandler().HandleAsync(Command(token), CancellationToken.None);
        var unknown = await NewHandler().HandleAsync(Command("not-a-token-anybody-issued"), CancellationToken.None);

        Assert.False(replayed.IsSuccess);
        Assert.Equal(unknown.Error, replayed.Error);
    }

    /// <summary>An expired token is refused.</summary>
    [Fact]
    public async Task An_expired_token_is_refused()
    {
        var token = await IssueTokenAsync();
        _clock.Advance(InvitationToken.Lifetime + TimeSpan.FromMinutes(1));

        var result = await NewHandler().HandleAsync(Command(token), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("InvitationTokenInvalid", result.Error!.Code);
    }

    /// <summary>Accepting retires every other outstanding token of that user.</summary>
    [Fact]
    public async Task Accepting_retires_every_other_outstanding_token_of_that_user()
    {
        var first = await IssueTokenAsync();
        var second = await IssueTokenAsync();

        await NewHandler().HandleAsync(Command(first), CancellationToken.None);

        Assert.All(await _context.InvitationTokens.ToListAsync(), stored =>
        {
            Assert.NotNull(stored.UsedAt);
        });

        // The retired sibling is now unusable too, not merely marked: presenting it must be refused
        // exactly as a token that had never existed would be.
        var secondAttempt = await NewHandler().HandleAsync(Command(second), CancellationToken.None);
        Assert.False(secondAttempt.IsSuccess);
    }

    /// <summary>A refused token is journalled without the token or the chosen password.</summary>
    [Fact]
    public async Task A_refused_token_is_journalled_without_the_token_or_the_chosen_password()
    {
        const string Presented = "not-a-token-anybody-issued";

        await NewHandler().HandleAsync(Command(Presented), CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.False(entry.Succeeded);
        Assert.DoesNotContain(Presented, entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(NewPassword, entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>A completed acceptance is journalled without the token or the password.</summary>
    [Fact]
    public async Task A_completed_acceptance_is_journalled_without_the_token_or_the_password()
    {
        var token = await IssueTokenAsync();

        await NewHandler().HandleAsync(Command(token), CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(AuditActions.InvitationAccepted, entry.Action);
        Assert.True(entry.Succeeded);
        Assert.DoesNotContain(token, entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(NewPassword, entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>
    /// Accepting an invitation while the hosting account is suspended stores the chosen password but
    /// does NOT produce a usable login: the account created, invited, then suspended before the
    /// owner opened the link must not become <see cref="UserState.Active"/> just because the token is
    /// still live.
    /// </summary>
    /// <remarks>
    /// Closes the hole a final review found: <c>User.Suspend()</c> deliberately leaves an
    /// <see cref="UserState.Invited"/> login in <c>Invited</c> rather than consuming the invitation,
    /// but nothing previously stopped <c>Activate</c> from later making it fully usable regardless.
    /// </remarks>
    [Fact]
    public async Task Accepting_an_invitation_on_a_suspended_account_does_not_activate_the_login()
    {
        var token = await IssueTokenAsync();
        var user = await _context.Users.SingleAsync(candidate => candidate.Id == _userId);
        user.Suspend();
        await _context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command(token), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await _context.Users.SingleAsync(candidate => candidate.Id == _userId);
        Assert.Equal(UserState.Suspended, stored.State);
        Assert.NotEqual(UserState.Active, stored.State);
        // The password was still stored: once the account is resumed the owner may sign in with the
        // password they already chose, rather than needing a second invitation.
        Assert.True(_hasher.Verify(NewPassword, stored.PasswordHash));
    }

    /// <summary>
    /// Once the account is resumed, the login the owner already completed while suspended becomes
    /// usable — no second invitation is needed.
    /// </summary>
    [Fact]
    public async Task Resuming_the_account_makes_the_already_accepted_login_active()
    {
        var token = await IssueTokenAsync();
        var user = await _context.Users.SingleAsync(candidate => candidate.Id == _userId);
        user.Suspend();
        await _context.SaveChangesAsync();

        await NewHandler().HandleAsync(Command(token), CancellationToken.None);

        var stored = await _context.Users.SingleAsync(candidate => candidate.Id == _userId);
        stored.Resume();
        await _context.SaveChangesAsync();

        var resumed = await _context.Users.SingleAsync(candidate => candidate.Id == _userId);
        Assert.Equal(UserState.Active, resumed.State);
        Assert.True(_hasher.Verify(NewPassword, resumed.PasswordHash));
    }
}
