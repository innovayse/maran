using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.IntegrationEvents.Handlers;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.IntegrationEvents.Handlers;

/// <summary>
/// Behavioural contract of the handler that issues a hosting account owner's panel login and their
/// invitation mail when the account is created.
/// </summary>
public sealed class AccountCreatedHandlerTests : IDisposable
{
    private const string InitialPassword = "first horse battery staple";

    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly RecordingAuditWriter _audit = new();
    private readonly RecordingMessageBus _bus = new();
    private readonly FakeClock _clock = new(Now);

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private static AccountCreated Created()
    {
        return new AccountCreated(AccountId, "owner-one", "owner@example.com", "en");
    }

    private static AccountCreated CreatedWithPassword(string? password = InitialPassword, string username = "owner-one")
    {
        return new AccountCreated(
            AccountId,
            username,
            "owner@example.com",
            "en",
            OwnerLoginMode.InitialPassword,
            password is null ? null : new SensitiveString(password));
    }

    private AccountCreatedHandler NewHandler(string panelUrl = "")
    {
        return new AccountCreatedHandler(
            _context,
            new AccountOwnerFinder(_context),
            _bus,
            new IdentityAuditJournal(_audit, new StubCurrentUser()),
            new InvitationMailComposer(
                new StubEmailTemplates(), new OptionsWrapper<PanelOptions>(new PanelOptions { PublicUrl = panelUrl })),
            _clock,
            new Argon2idPasswordHasher(),
            new InitialPasswordPolicy(TestSecurityPolicyCache.Over(_context)));
    }

    /// <summary>A created account yields exactly one invited login, one live token, and one published mail.</summary>
    [Fact]
    public async Task A_created_account_yields_one_invited_login_one_live_token_and_one_published_mail()
    {
        await NewHandler().HandleAsync(Created(), CancellationToken.None);

        var user = Assert.Single(await _context.Users.ToListAsync());
        Assert.Equal(UserLifecycle.Invited, user.Lifecycle);
        Assert.Equal(AccountId, Assert.Single(await _context.AccountMemberships.ToListAsync()).AccountId);
        var token = Assert.Single(await _context.InvitationTokens.Where(candidate => candidate.UserId == user.Id).ToListAsync());
        Assert.True(token.IsUsable(Now));
        Assert.Single(_bus.Published);
    }

    /// <summary>
    /// An imported account (issue #127) still gets its login and a live token, so the administrator can issue
    /// the link at cut-over, but no mail is published: the owner is not invited to a server that is not live.
    /// The default test above is the inverse control, where the same event without the flag does publish.
    /// </summary>
    [Fact]
    public async Task A_suppressed_invitation_makes_the_login_and_the_token_and_publishes_no_mail()
    {
        await NewHandler().HandleAsync(Created() with { SuppressInvitationMail = true }, CancellationToken.None);

        var user = Assert.Single(await _context.Users.ToListAsync());
        Assert.Equal(UserLifecycle.Invited, user.Lifecycle);
        var token = Assert.Single(await _context.InvitationTokens.Where(candidate => candidate.UserId == user.Id).ToListAsync());
        Assert.True(token.IsUsable(Now));
        Assert.Empty(_bus.Published);
    }

    /// <summary>A created account gives its owner an owner membership, written with the login.</summary>
    [Fact]
    public async Task A_created_account_gives_its_owner_an_owner_membership()
    {
        await NewHandler().HandleAsync(Created(), CancellationToken.None);

        var user = await _context.Users.SingleAsync();
        var membership = Assert.Single(await _context.AccountMemberships.ToListAsync());
        Assert.Equal(user.Id, membership.UserId);
        Assert.Equal(AccountId, membership.AccountId);
        Assert.Equal(MembershipRole.Owner, membership.Role);
    }

    /// <summary>Invoked twice, the handler leaves one membership, not two.</summary>
    [Fact]
    public async Task Invoked_twice_the_handler_leaves_one_membership()
    {
        var handler = NewHandler();
        await handler.HandleAsync(Created(), CancellationToken.None);

        await handler.HandleAsync(Created(), CancellationToken.None);

        Assert.Equal(1, await _context.AccountMemberships.CountAsync());
    }

    /// <summary>The handler is idempotent: invoked twice, one login and one live token survive.</summary>
    /// <remarks>
    /// This pins the check, not the constraint that actually stops a duplicate — the in-memory
    /// provider has no unique index, so a defect that dropped the pre-check would fail this test
    /// even though it would pass against real PostgreSQL, which is the point of testing the ordinary
    /// path here at all.
    /// </remarks>
    [Fact]
    public async Task The_handler_is_idempotent_invoked_twice_one_login_and_one_live_token()
    {
        var handler = NewHandler();
        await handler.HandleAsync(Created(), CancellationToken.None);

        await handler.HandleAsync(Created(), CancellationToken.None);

        Assert.Equal(1, await _context.Users.CountAsync());
        Assert.Equal(1, await _context.InvitationTokens.CountAsync());
        Assert.Single(_bus.Published);
    }

    /// <summary>The published mail's body contains the token; the audit entry does not.</summary>
    /// <remarks>
    /// The absence is asserted explicitly, on the actual token value, so a defect that logged or
    /// journalled the digest — or the plaintext itself — would fail this rather than merely fail to
    /// be caught by it.
    /// </remarks>
    [Fact]
    public async Task The_published_mails_body_contains_the_token_the_audit_entry_does_not()
    {
        await NewHandler(panelUrl: "https://panel.example.com").HandleAsync(Created(), CancellationToken.None);

        var mail = Assert.IsType<SendMailRequested>(Assert.Single(_bus.Published));
        var token = await _context.InvitationTokens.SingleAsync();

        // The plaintext token is never stored; it is recovered here only to prove the mail carries
        // it, by re-deriving the digest of every candidate is not possible, so instead the assertion
        // works from the other end: the mail must contain SOME token, and that token must hash to
        // the one row this handler created.
        var embedded = ExtractToken(mail.Body);
        Assert.Equal(token.TokenHash, Maran.SharedKernel.Utilities.Tokens.InvitationTokenHasher.Hash(embedded));

        var entry = Assert.Single(_audit.Written);
        Assert.DoesNotContain(embedded, entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(embedded, entry.ActorUsername, StringComparison.Ordinal);
        Assert.DoesNotContain(token.TokenHash, entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(token.TokenHash, entry.ActorUsername, StringComparison.Ordinal);
    }

    /// <summary>An initial password makes a usable login that must change it, with no token and no mail.</summary>
    [Fact]
    public async Task An_initial_password_makes_a_usable_login_that_must_change_it_with_no_token_and_no_mail()
    {
        await NewHandler().HandleAsync(CreatedWithPassword(), CancellationToken.None);

        var user = await _context.Users.SingleAsync();
        Assert.Equal(UserLifecycle.Active, user.Lifecycle);
        Assert.True(user.PasswordChangeRequired);
        Assert.True(new Argon2idPasswordHasher().Verify(InitialPassword, user.PasswordHash));
        Assert.Equal(AccountId, Assert.Single(await _context.AccountMemberships.ToListAsync()).AccountId);
        Assert.Empty(await _context.InvitationTokens.ToListAsync());
        Assert.Empty(_bus.Published);
    }

    /// <summary>An invitation leaves a login that owes no password change: the accepting control for the branch.</summary>
    [Fact]
    public async Task An_invitation_leaves_a_login_that_owes_no_password_change()
    {
        await NewHandler().HandleAsync(Created(), CancellationToken.None);

        Assert.False((await _context.Users.SingleAsync()).PasswordChangeRequired);
    }

    /// <summary>The entry journals the mode and never the password or its hash.</summary>
    [Fact]
    public async Task The_entry_names_the_mode_and_never_carries_the_password_or_its_hash()
    {
        await NewHandler().HandleAsync(CreatedWithPassword(), CancellationToken.None);

        var user = await _context.Users.SingleAsync();
        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.CustomerCreatedWithPassword, entry.Action);
        Assert.Equal("owner-one", entry.Subject);
        Assert.DoesNotContain(InitialPassword, entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(InitialPassword, entry.ActorUsername, StringComparison.Ordinal);
        Assert.DoesNotContain(user.PasswordHash, entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(user.PasswordHash, entry.ActorUsername, StringComparison.Ordinal);
    }

    /// <summary>The plaintext password is stored nowhere in the Identity tables.</summary>
    /// <remarks>
    /// A planted-password probe over every string the handler leaves behind, with its positive control:
    /// the same probe finds the username the handler stored, so a probe that looked at nothing would fail.
    /// </remarks>
    [Fact]
    public async Task The_plaintext_password_is_stored_nowhere()
    {
        await NewHandler().HandleAsync(CreatedWithPassword(), CancellationToken.None);

        var user = await _context.Users.SingleAsync();
        var stored = new List<string>
        {
            user.Username,
            user.Email,
            user.PasswordHash,
            user.TotpSecret ?? string.Empty,
        };

        Assert.Contains(stored, value => { return value.Contains("owner-one", StringComparison.Ordinal); });
        Assert.DoesNotContain(stored, value => { return value.Contains(InitialPassword, StringComparison.Ordinal); });
    }

    /// <summary>The event prints the password as a mask, so logging it leaks nothing.</summary>
    [Fact]
    public void The_event_prints_the_password_as_a_mask()
    {
        var text = CreatedWithPassword().ToString();

        Assert.DoesNotContain(InitialPassword, text, StringComparison.Ordinal);
        Assert.Contains("[redacted]", text, StringComparison.Ordinal);
    }

    /// <summary>Invoked twice with a password, the handler leaves one login.</summary>
    [Fact]
    public async Task Invoked_twice_with_a_password_the_handler_leaves_one_login()
    {
        var handler = NewHandler();
        await handler.HandleAsync(CreatedWithPassword(), CancellationToken.None);

        await handler.HandleAsync(CreatedWithPassword(), CancellationToken.None);

        Assert.Equal(1, await _context.Users.CountAsync());
        Assert.Single(_audit.Written);
    }

    /// <summary>A password shorter than the operator's minimum creates no login.</summary>
    [Fact]
    public async Task A_password_shorter_than_the_minimum_creates_no_login()
    {
        var tooShort = new string('x', SecurityPolicy.DefaultMinimumPasswordLength - 1);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await NewHandler().HandleAsync(CreatedWithPassword(tooShort), CancellationToken.None);
        });

        Assert.DoesNotContain(tooShort, thrown.Message, StringComparison.Ordinal);
        Assert.Empty(await _context.Users.ToListAsync());
        Assert.Empty(await _context.AccountMemberships.ToListAsync());
        Assert.Empty(_audit.Written);
    }

    /// <summary>A password exactly as long as the minimum is accepted: the boundary, from the other side.</summary>
    [Fact]
    public async Task A_password_exactly_as_long_as_the_minimum_is_accepted()
    {
        var exact = new string('x', SecurityPolicy.DefaultMinimumPasswordLength);

        await NewHandler().HandleAsync(CreatedWithPassword(exact), CancellationToken.None);

        Assert.Single(await _context.Users.ToListAsync());
    }

    /// <summary>A password equal to the login name creates no login.</summary>
    [Fact]
    public async Task A_password_equal_to_the_username_creates_no_login()
    {
        var username = new string('u', SecurityPolicy.DefaultMinimumPasswordLength);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await NewHandler().HandleAsync(CreatedWithPassword(username, username), CancellationToken.None);
        });

        Assert.Empty(await _context.Users.ToListAsync());
    }

    /// <summary>A password over the ceiling creates no login.</summary>
    [Fact]
    public async Task A_password_over_the_ceiling_creates_no_login()
    {
        var tooLong = new string('x', PasswordPolicy.MaxLength + 1);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await NewHandler().HandleAsync(CreatedWithPassword(tooLong), CancellationToken.None);
        });

        Assert.Empty(await _context.Users.ToListAsync());
    }

    /// <summary>The initial-password mode without a password creates nothing and never falls through to an invitation.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task The_initial_password_mode_without_a_password_creates_nothing(string? password)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await NewHandler().HandleAsync(CreatedWithPassword(password), CancellationToken.None);
        });

        Assert.Empty(await _context.Users.ToListAsync());
        Assert.Empty(await _context.InvitationTokens.ToListAsync());
        Assert.Empty(_bus.Published);
    }

    /// <summary>A password supplied for the invitation mode is refused, not silently ignored.</summary>
    [Fact]
    public async Task A_password_supplied_for_the_invitation_mode_is_refused()
    {
        var contradictory = new AccountCreated(
            AccountId, "owner-one", "owner@example.com", "en", OwnerLoginMode.Invitation, new SensitiveString(InitialPassword));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await NewHandler().HandleAsync(contradictory, CancellationToken.None);
        });

        Assert.Empty(await _context.Users.ToListAsync());
        Assert.Empty(_bus.Published);
    }

    /// <summary>Pulls the token query string value out of the composed invitation link.</summary>
    /// <param name="body">The mail body, as <see cref="StubEmailTemplates"/> rendered it.</param>
    /// <returns>The plaintext token embedded in the link.</returns>
    private static string ExtractToken(string body)
    {
        const string Marker = "token=";
        var start = body.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length;
        var end = body.IndexOf('"', start);
        var raw = end >= 0 ? body[start..end] : body[start..];
        return Uri.UnescapeDataString(raw.TrimEnd(')', ' '));
    }
}
