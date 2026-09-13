using Maran.Modules.Identity.Commands.RequestPasswordReset;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Commands.RequestPasswordReset;

/// <summary>Behavioural contract of the password-reset request handler.</summary>
public sealed class RequestPasswordResetCommandHandlerTests : IDisposable
{
    private const string KnownEmail = "admin@example.com";
    private const string UnknownEmail = "nobody@example.com";

    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly RecordingMessageBus _bus = new();
    private readonly RecordingAuditWriter _audit = new();
    private readonly FakeClock _clock = new(Now);

    /// <summary>Releases the in-memory context this test owns.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private RequestPasswordResetCommandHandler NewHandler(string panelUrl = "")
    {
        return new RequestPasswordResetCommandHandler(
            _context,
            _bus,
            new IdentityAuditJournal(_audit, new StubCurrentUser()),
            new StubEmailTemplates(),
            new OptionsWrapper<PasswordResetOptions>(new PasswordResetOptions { PanelUrl = panelUrl }),
            _clock);
    }

    private async Task<User> SeedUserAsync()
    {
        var user = new User(Guid.NewGuid(), "admin", KnownEmail, "hash", UserRole.Admin, Now);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    private static RequestPasswordResetCommand Command(string email)
    {
        return new RequestPasswordResetCommand(email, "203.0.113.7", "agent");
    }

    /// <summary>A known and an unknown address get the identical result.</summary>
    [Fact]
    public async Task A_known_and_an_unknown_address_get_the_identical_result()
    {
        await SeedUserAsync();

        var known = await NewHandler().HandleAsync(Command(KnownEmail), CancellationToken.None);
        var unknown = await NewHandler().HandleAsync(Command(UnknownEmail), CancellationToken.None);

        Assert.True(known.IsSuccess);
        Assert.True(unknown.IsSuccess);
        Assert.Equal(known.Value, unknown.Value);
        Assert.Null(known.Error);
        Assert.Null(unknown.Error);
    }

    /// <summary>An unknown address publishes no mail and stores no token.</summary>
    [Fact]
    public async Task An_unknown_address_publishes_no_mail_and_stores_no_token()
    {
        await SeedUserAsync();

        await NewHandler().HandleAsync(Command(UnknownEmail), CancellationToken.None);

        Assert.Empty(_bus.Published);
        Assert.Empty(await _context.PasswordResetTokens.ToListAsync());
    }

    /// <summary>The reset mail is published and never sent by this handler.</summary>
    /// <remarks>
    /// The bus double throws from every <c>InvokeAsync</c> overload, so a handler that sent inline —
    /// the shape R11 forbids, and the one that turns a known address into a seconds-long response —
    /// cannot reach the end of this test.
    /// </remarks>
    [Fact]
    public async Task The_reset_mail_is_published_and_never_sent_by_this_handler()
    {
        var user = await SeedUserAsync();

        await NewHandler().HandleAsync(Command(KnownEmail), CancellationToken.None);

        var published = Assert.IsType<SendMailRequested>(Assert.Single(_bus.Published));
        Assert.Equal(user.Email, published.Recipient);
    }

    /// <summary>The stored token is a digest and the plaintext appears only in the mail.</summary>
    [Fact]
    public async Task The_stored_token_is_a_digest_and_the_plaintext_appears_only_in_the_mail()
    {
        await SeedUserAsync();

        await NewHandler().HandleAsync(Command(KnownEmail), CancellationToken.None);

        var stored = Assert.Single(await _context.PasswordResetTokens.ToListAsync());
        var published = (SendMailRequested)Assert.Single(_bus.Published);

        Assert.DoesNotContain(stored.TokenHash, published.Body, StringComparison.Ordinal);
        Assert.Equal(Now + PasswordResetToken.Lifetime, stored.ExpiresAt);
        Assert.Null(stored.UsedAt);
    }

    /// <summary>Asking twice leaves only the newest token usable.</summary>
    [Fact]
    public async Task Asking_twice_leaves_only_the_newest_token_usable()
    {
        await SeedUserAsync();

        await NewHandler().HandleAsync(Command(KnownEmail), CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await NewHandler().HandleAsync(Command(KnownEmail), CancellationToken.None);

        var tokens = await _context.PasswordResetTokens.ToListAsync();
        Assert.Equal(2, tokens.Count);
        Assert.Single(tokens, token =>
        {
            return token.IsUsable(_clock.UtcNow);
        });
    }

    /// <summary>Every request is journalled whether or not the address belongs to anybody.</summary>
    [Fact]
    public async Task Every_request_is_journalled_whether_or_not_the_address_belongs_to_anybody()
    {
        var user = await SeedUserAsync();

        await NewHandler().HandleAsync(Command(KnownEmail), CancellationToken.None);
        await NewHandler().HandleAsync(Command(UnknownEmail), CancellationToken.None);

        Assert.Equal(2, _audit.Written.Count);
        Assert.All(_audit.Written, entry =>
        {
            Assert.Equal(AuditActions.PasswordResetRequested, entry.Action);
        });
        Assert.Equal(user.Id, _audit.Written[0].ActorUserId);
        Assert.Null(_audit.Written[1].ActorUserId);
    }

    /// <summary>The live reset token reaches the mail and no field of the journal entry.</summary>
    /// <remarks>
    /// The token is a live permission to take over the account, and the journal is append-only and
    /// never deleted (rules/security.md item 8). Asserting only that the subject is clean would miss
    /// a token written into the actor column, which is the column this endpoint fills from the
    /// caller's own text.
    /// </remarks>
    [Fact]
    public async Task The_live_reset_token_reaches_the_mail_and_no_field_of_the_journal_entry()
    {
        await SeedUserAsync();

        await NewHandler().HandleAsync(Command(KnownEmail), CancellationToken.None);

        var body = ((SendMailRequested)_bus.Published.Single()).Body;
        var token = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(line =>
            {
                return line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            })
            .Single(word =>
            {
                return word.Length >= 32;
            });

        var entry = Assert.Single(_audit.Written);
        Assert.DoesNotContain(token, entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(token, entry.ActorUsername, StringComparison.Ordinal);
        Assert.DoesNotContain(token, entry.IpAddress, StringComparison.Ordinal);
        Assert.DoesNotContain(token, entry.UserAgent, StringComparison.Ordinal);
        Assert.DoesNotContain(token, entry.Action, StringComparison.Ordinal);
    }

    /// <summary>The mail carries a link only when the panel has been told its own address.</summary>
    /// <remarks>
    /// The alternative source for the link is the request's Host header, which the caller supplies:
    /// a reset link built from it points wherever the attacker says, in the panel's own name.
    /// </remarks>
    [Fact]
    public async Task The_mail_carries_a_link_only_when_the_panel_has_been_told_its_own_address()
    {
        await SeedUserAsync();

        await NewHandler("https://panel.example.com/").HandleAsync(Command(KnownEmail), CancellationToken.None);
        var withUrl = (SendMailRequested)_bus.Published[0];

        _bus.Published.Clear();
        await NewHandler().HandleAsync(Command(KnownEmail), CancellationToken.None);
        var withoutUrl = (SendMailRequested)_bus.Published[0];

        Assert.Contains("https://panel.example.com/reset-password?token=", withUrl.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("http", withoutUrl.Body, StringComparison.Ordinal);
    }
}
