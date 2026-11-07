using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.IntegrationEvents.Handlers;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Maran.SharedKernel.Utilities.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.IntegrationEvents.Handlers;

/// <summary>
/// Behavioural contract of the handler that issues a reseller's panel login and its invitation mail
/// when the reseller is created (issue #179).
/// </summary>
public sealed class ResellerCreatedHandlerTests : IDisposable
{
    private static readonly Guid ResellerId = Guid.Parse("eeeeeeee-0000-4000-8000-000000000001");

    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly RecordingAuditWriter _audit = new();
    private readonly RecordingMessageBus _bus = new();
    private readonly FakeClock _clock = new(Now);

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private static ResellerCreated Created()
    {
        return new ResellerCreated(ResellerId, "northwind", "contact@example.com", "en");
    }

    private ResellerCreatedHandler NewHandler(IdentityDbContext? context = null, string panelUrl = "")
    {
        return new ResellerCreatedHandler(
            context ?? _context,
            _bus,
            new IdentityAuditJournal(_audit, new StubCurrentUser()),
            new InvitationMailComposer(
                new StubEmailTemplates(), new OptionsWrapper<PanelOptions>(new PanelOptions { PublicUrl = panelUrl })),
            _clock);
    }

    /// <summary>A created reseller yields exactly one invited reseller login, one live token and one published mail.</summary>
    [Fact]
    public async Task A_created_reseller_yields_one_invited_login_one_live_token_and_one_published_mail()
    {
        await NewHandler().HandleAsync(Created(), CancellationToken.None);

        var user = Assert.Single(await _context.Users.ToListAsync());
        Assert.Equal(UserRole.Reseller, user.Role);
        Assert.Equal(ResellerId, user.ResellerId);
        Assert.Equal("northwind", user.Username);
        Assert.Equal("contact@example.com", user.Email);
        Assert.Equal(UserLifecycle.Invited, user.Lifecycle);
        var token = Assert.Single(await _context.InvitationTokens.Where(candidate => candidate.UserId == user.Id).ToListAsync());
        Assert.True(token.IsUsable(Now));
        Assert.Single(_bus.Published);
    }

    /// <summary>A reseller's login holds no membership: nothing is written to the accounts it may reach.</summary>
    [Fact]
    public async Task A_created_reseller_login_holds_no_membership()
    {
        await NewHandler().HandleAsync(Created(), CancellationToken.None);

        Assert.Empty(await _context.AccountMemberships.ToListAsync());
    }

    /// <summary>The invitation is the reseller's own template, addressed to the contact, and the issuance is journalled.</summary>
    [Fact]
    public async Task The_mail_is_the_reseller_invitation_addressed_to_the_contact_and_the_invitation_is_journalled()
    {
        await NewHandler(panelUrl: "https://panel.example.com").HandleAsync(Created(), CancellationToken.None);

        var mail = Assert.IsType<SendMailRequested>(Assert.Single(_bus.Published));
        Assert.Equal("contact@example.com", mail.Recipient);
        Assert.StartsWith("ResellerInvitationSubject", mail.Subject, StringComparison.Ordinal);
        Assert.StartsWith("ResellerInvitationBody", mail.Body, StringComparison.Ordinal);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.ResellerInvited, entry.Action);
        Assert.Equal("northwind", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>The mail carries the token that hashes to the stored row, and the journal never carries it.</summary>
    [Fact]
    public async Task The_mail_carries_the_token_the_row_hashes_and_the_journal_does_not()
    {
        await NewHandler(panelUrl: "https://panel.example.com").HandleAsync(Created(), CancellationToken.None);

        var mail = Assert.IsType<SendMailRequested>(Assert.Single(_bus.Published));
        var stored = await _context.InvitationTokens.SingleAsync();
        var embedded = ExtractToken(mail.Body);
        Assert.Equal(stored.TokenHash, InvitationTokenHasher.Hash(embedded));

        var entry = Assert.Single(_audit.Written);
        Assert.DoesNotContain(embedded, entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(embedded, entry.ActorUsername, StringComparison.Ordinal);
        Assert.DoesNotContain(stored.TokenHash, entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>The same event twice leaves one login, one live token and one mail.</summary>
    [Fact]
    public async Task The_same_event_twice_leaves_one_login_one_token_and_one_mail()
    {
        var handler = NewHandler();
        await handler.HandleAsync(Created(), CancellationToken.None);

        await handler.HandleAsync(Created(), CancellationToken.None);

        Assert.Equal(1, await _context.Users.CountAsync());
        Assert.Equal(1, await _context.InvitationTokens.CountAsync());
        Assert.Single(_bus.Published);
        Assert.Single(_audit.Written);
    }

    /// <summary>A delivery that loses the race on the unique index finds the winner's login and does nothing more.</summary>
    /// <remarks>
    /// The in-memory provider has no unique index, so the loss is simulated: the interceptor stores
    /// the winner's login through a second context over the same database, then fails the save the
    /// way PostgreSQL does. The loser must publish no mail, since the winner's token is the live one.
    /// </remarks>
    [Fact]
    public async Task A_delivery_that_loses_the_race_leaves_the_winners_login_and_publishes_nothing()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var winner = IdentityTestContext.CreateOver(databaseName);
        using var loser = IdentityTestContext.CreateOver(
            databaseName,
            new UniqueViolationOnSaveInterceptor(() =>
            {
                winner.Users.Add(User.InviteReseller(Guid.NewGuid(), "northwind", "contact@example.com", ResellerId, Now));
                winner.SaveChanges();
            }));

        await NewHandler(loser).HandleAsync(Created(), CancellationToken.None);

        Assert.Equal(1, await winner.Users.CountAsync());
        Assert.Empty(await winner.InvitationTokens.ToListAsync());
        Assert.Empty(_bus.Published);
        Assert.Empty(_audit.Written);
    }

    /// <summary>
    /// A unique violation with no login for this reseller behind it is a real failure — the name or
    /// the address belongs to another login — and is thrown, not swallowed.
    /// </summary>
    [Fact]
    public async Task A_unique_violation_with_no_login_for_the_reseller_is_rethrown()
    {
        using var failing = IdentityTestContext.CreateOver(
            Guid.NewGuid().ToString(),
            new UniqueViolationOnSaveInterceptor());

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await NewHandler(failing).HandleAsync(Created(), CancellationToken.None);
        });

        Assert.Empty(_bus.Published);
        Assert.Empty(_audit.Written);
        Assert.Empty(await failing.Users.ToListAsync());
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
