using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.IntegrationEvents.Handlers;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.IntegrationEvents.Handlers;

/// <summary>
/// Behavioural contract of the handler that issues a hosting account owner's panel login and their
/// invitation mail when the account is created.
/// </summary>
public sealed class AccountCreatedHandlerTests : IDisposable
{
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
        return new AccountCreated(AccountId, "owner-one", "owner@example.com");
    }

    private AccountCreatedHandler NewHandler(string panelUrl = "")
    {
        return new AccountCreatedHandler(
            _context,
            _bus,
            new IdentityAuditJournal(_audit, new StubCurrentUser()),
            new InvitationMailComposer(
                new StubEmailTemplates(), new OptionsWrapper<PanelOptions>(new PanelOptions { PanelUrl = panelUrl })),
            _clock);
    }

    /// <summary>A created account yields exactly one invited login, one live token, and one published mail.</summary>
    [Fact]
    public async Task A_created_account_yields_one_invited_login_one_live_token_and_one_published_mail()
    {
        await NewHandler().HandleAsync(Created(), CancellationToken.None);

        var user = Assert.Single(await _context.Users.Where(candidate => candidate.AccountId == AccountId).ToListAsync());
        Assert.Equal(UserState.Invited, user.State);
        var token = Assert.Single(await _context.InvitationTokens.Where(candidate => candidate.UserId == user.Id).ToListAsync());
        Assert.True(token.IsUsable(Now));
        Assert.Single(_bus.Published);
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

        Assert.Equal(1, await _context.Users.CountAsync(candidate => candidate.AccountId == AccountId));
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
