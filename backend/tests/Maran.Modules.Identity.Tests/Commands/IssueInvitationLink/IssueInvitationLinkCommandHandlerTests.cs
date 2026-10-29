using Maran.Modules.Identity.Commands.IssueInvitationLink;
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

namespace Maran.Modules.Identity.Tests.Commands.IssueInvitationLink;

/// <summary>
/// Behavioral contract of <see cref="IssueInvitationLinkCommandHandler"/>, written against the claims
/// of its threat note (<c>docs/superpowers/notes/2026-10-26-invitation-link-threat-note.md</c>): a link
/// is issued only for a login that has not been claimed, it is re-minted rather than recovered so the
/// plaintext exists in the response and nowhere else, the previous link stops working, and every
/// attempt — granted or refused — is journalled without the link.
/// </summary>
public sealed class IssueInvitationLinkCommandHandlerTests : IDisposable
{
    /// <summary>The hosting account whose owner is invited.</summary>
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    /// <summary>The fixed instant the clock reads.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The module's context, in memory.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The bus; the handler publishes nothing to it, and a test says so.</summary>
    private readonly RecordingMessageBus _bus = new();

    /// <summary>The window onto the Accounts module.</summary>
    private readonly StubAccountDirectory _accountDirectory = new();

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
    /// <param name="panelUrl">The configured public address of the panel; empty when none is.</param>
    private IssueInvitationLinkCommandHandler NewHandler(string panelUrl = "https://panel.example.com")
    {
        return new IssueInvitationLinkCommandHandler(
            new InvitationIssuer(_context, _accountDirectory),
            new InvitationLinkBuilder(new OptionsWrapper<PanelOptions>(new PanelOptions { PanelUrl = panelUrl })),
            new IdentityAuditJournal(_audit, _admin),
            _clock,
            _admin);
    }

    /// <summary>Builds the request for the account under test.</summary>
    private static IssueInvitationLinkCommand Command()
    {
        return new IssueInvitationLinkCommand(AccountId, "203.0.113.7", "unit-tests");
    }

    /// <summary>Stores an invited login for the account, with a token whose plaintext the test knows.</summary>
    /// <param name="token">The plaintext of the invitation token already outstanding.</param>
    private async Task<User> SeedInvitedLoginAsync(string token)
    {
        var user = User.Invite(Guid.NewGuid(), "owner-one", "owner@example.com", AccountId, Now);
        _context.Users.Add(user);
        _context.InvitationTokens.Add(new InvitationToken(Guid.NewGuid(), user.Id, InvitationTokenHasher.Hash(token), Now));
        await _context.SaveChangesAsync();
        return user;
    }

    /// <summary>Pulls the plaintext token out of an invitation link.</summary>
    /// <param name="link">The link, absolute or root-relative.</param>
    private static string TokenIn(string link)
    {
        const string Marker = "token=";
        return Uri.UnescapeDataString(link[(link.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length)..]);
    }

    /// <summary>An unused invitation yields a link whose token is the one now stored, as a digest.</summary>
    [Fact]
    public async Task An_unused_invitation_yields_a_link_whose_token_is_the_one_now_stored_as_a_digest()
    {
        await SeedInvitedLoginAsync("the-mailed-token");

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var link = result.Value.Link.Reveal();
        Assert.StartsWith("https://panel.example.com/accept-invitation?token=", link, StringComparison.Ordinal);
        var live = Assert.Single(await _context.InvitationTokens.Where(token => token.UsedAt == null).ToListAsync());
        Assert.Equal(InvitationTokenHasher.Hash(TokenIn(link)), live.TokenHash);
        Assert.True(live.IsUsable(Now));
        Assert.Equal(Now + InvitationToken.Lifetime, result.Value.ExpiresAt);
    }

    /// <summary>Without a configured panel address the link is root-relative.</summary>
    [Fact]
    public async Task Without_a_configured_panel_address_the_link_is_root_relative()
    {
        // The address comes from configuration, never from the request's Host header (the host-header
        // injection argument the threat note makes), so an unconfigured panel hands back a path the
        // SPA prefixes with its own origin rather than guessing a host.
        await SeedInvitedLoginAsync("the-mailed-token");

        var result = await NewHandler(panelUrl: string.Empty).HandleAsync(Command(), CancellationToken.None);

        Assert.StartsWith("/accept-invitation?token=", result.Value.Link.Reveal(), StringComparison.Ordinal);
    }

    /// <summary>No plaintext token or link rests in any stored invitation row.</summary>
    [Fact]
    public async Task No_plaintext_token_or_link_rests_in_any_stored_invitation_row()
    {
        await SeedInvitedLoginAsync("the-mailed-token");

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var link = result.Value.Link.Reveal();
        var token = TokenIn(link);
        // Positive control first: the probe must be able to see a token in this store at all, or an
        // empty answer below means nothing. The stored digest of the issued token is found by hashing.
        var rows = await _context.InvitationTokens.AsNoTracking().ToListAsync();
        Assert.Contains(rows, row =>
        {
            return row.TokenHash == InvitationTokenHasher.Hash(token);
        });
        Assert.All(rows, row =>
        {
            Assert.NotEqual(token, row.TokenHash);
            Assert.DoesNotContain(token, row.TokenHash, StringComparison.Ordinal);
        });
    }

    /// <summary>Issuing a link retires the previous one and leaves exactly one live token.</summary>
    [Fact]
    public async Task Issuing_a_link_retires_the_previous_one_and_leaves_exactly_one_live_token()
    {
        await SeedInvitedLoginAsync("the-mailed-token");
        var first = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        _clock.Advance(TimeSpan.FromMinutes(1));
        var second = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var firstToken = TokenIn(first.Value.Link.Reveal());
        var secondToken = TokenIn(second.Value.Link.Reveal());
        Assert.NotEqual(firstToken, secondToken);
        var rows = await _context.InvitationTokens.AsNoTracking().ToListAsync();
        Assert.Equal(3, rows.Count);
        var usable = Assert.Single(rows, row =>
        {
            return row.IsUsable(_clock.UtcNow);
        });
        Assert.Equal(InvitationTokenHasher.Hash(secondToken), usable.TokenHash);
        var retired = Assert.Single(rows, row =>
        {
            return row.TokenHash == InvitationTokenHasher.Hash(firstToken);
        });
        Assert.False(retired.IsUsable(_clock.UtcNow));
        var mailed = Assert.Single(rows, row =>
        {
            return row.TokenHash == InvitationTokenHasher.Hash("the-mailed-token");
        });
        Assert.False(mailed.IsUsable(_clock.UtcNow));
    }

    /// <summary>An invitation whose token has lapsed can be replaced by a fresh link.</summary>
    [Fact]
    public async Task An_invitation_whose_token_has_lapsed_can_be_replaced_by_a_fresh_link()
    {
        // The case the feature exists for after a week: the mail never went out, the token expired,
        // and the administrator needs something to hand over now.
        await SeedInvitedLoginAsync("the-mailed-token");
        _clock.Advance(InvitationToken.Lifetime + TimeSpan.FromDays(1));

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var usable = Assert.Single(
            await _context.InvitationTokens.Where(token => token.UsedAt == null).ToListAsync(),
            token =>
            {
                return token.IsUsable(_clock.UtcNow);
            });
        Assert.Equal(InvitationTokenHasher.Hash(TokenIn(result.Value.Link.Reveal())), usable.TokenHash);
    }

    /// <summary>An account whose login was never created gets it, invited, from the account directory.</summary>
    [Fact]
    public async Task An_account_whose_login_was_never_created_gets_it_invited_from_the_account_directory()
    {
        _accountDirectory.Add(AccountId, "owner-one", maxFtpUsers: 1);

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var user = await _context.Users.SingleAsync(candidate => candidate.AccountId == AccountId);
        Assert.Equal(UserState.Invited, user.State);
        Assert.Single(await _context.InvitationTokens.Where(token => token.UserId == user.Id).ToListAsync());
    }

    /// <summary>A login that already has a password is refused and no token is minted for it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_login_that_already_has_a_password_is_refused_and_no_token_is_minted_for_it(bool suspended)
    {
        // Without this refusal the endpoint would be a password reset for any customer that skips the
        // reset endpoint's own rate limit; a suspended login already had a real password too.
        var user = await SeedInvitedLoginAsync("the-mailed-token");
        user.Activate("hash");
        if (suspended)
        {
            user.Suspend();
        }

        await _context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("InvitationLoginNotPending", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(1, await _context.InvitationTokens.CountAsync());
        Assert.Empty(_bus.Published);
    }

    /// <summary>An account that does not exist is refused as not found.</summary>
    [Fact]
    public async Task An_account_that_does_not_exist_is_refused_as_not_found()
    {
        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.Equal("InvitationAccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(await _context.InvitationTokens.ToListAsync());
    }

    /// <summary>A granted link is journalled as the administrator's act on the customer, without the link.</summary>
    [Fact]
    public async Task A_granted_link_is_journalled_as_the_administrators_act_on_the_customer_without_the_link()
    {
        await SeedInvitedLoginAsync("the-mailed-token");

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(AuditActions.InvitationLinkIssued, entry.Action);
        Assert.Equal("owner-one", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Equal(_admin.UserId, entry.ActorUserId);
        Assert.Equal("the-admin", entry.ActorUsername);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        var link = result.Value.Link.Reveal();
        var token = TokenIn(link);
        var digest = InvitationTokenHasher.Hash(token);
        foreach (var field in new[] { entry.Subject, entry.ActorUsername, entry.IpAddress, entry.UserAgent })
        {
            Assert.DoesNotContain(token, field, StringComparison.Ordinal);
            Assert.DoesNotContain(digest, field, StringComparison.Ordinal);
            Assert.DoesNotContain("accept-invitation", field, StringComparison.Ordinal);
        }
    }

    /// <summary>A refused attempt is journalled as a failure naming what was probed.</summary>
    [Fact]
    public async Task A_refused_attempt_is_journalled_as_a_failure_naming_what_was_probed()
    {
        var unknown = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.False(unknown.IsSuccess);
        Assert.Equal(AuditActions.InvitationLinkIssued, entry.Action);
        Assert.False(entry.Succeeded);
        Assert.Equal(AccountId.ToString("D"), entry.Subject);
        Assert.Equal(_admin.UserId, entry.ActorUserId);
    }

    /// <summary>An attempt on a login that already has a password is journalled as a failure.</summary>
    [Fact]
    public async Task An_attempt_on_a_login_that_already_has_a_password_is_journalled_as_a_failure()
    {
        var user = await SeedInvitedLoginAsync("the-mailed-token");
        user.Activate("hash");
        await _context.SaveChangesAsync();

        await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(AuditActions.InvitationLinkIssued, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>Nothing is mailed or published when a link is issued.</summary>
    [Fact]
    public async Task Nothing_is_mailed_or_published_when_a_link_is_issued()
    {
        // The threat note's envelope claim: the link travels as the return value and is never put on a
        // bus, whose durable tables would store it.
        await SeedInvitedLoginAsync("the-mailed-token");

        await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.Empty(_bus.Published);
    }

    /// <summary>The answer prints the link as redacted wherever a record's text is used.</summary>
    [Fact]
    public async Task The_answer_prints_the_link_as_redacted_wherever_a_records_text_is_used()
    {
        // The one reflexive way a value reaches a log: a record's generated ToString or an
        // interpolated message.
        await SeedInvitedLoginAsync("the-mailed-token");

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var token = TokenIn(result.Value.Link.Reveal());
        Assert.Contains("[redacted]", result.Value.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(token, result.Value.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(token, $"{result.Value.Link}", StringComparison.Ordinal);
    }
}
