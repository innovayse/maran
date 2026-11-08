using Maran.Modules.Identity.Commands.IssueResellerInvitationLink;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Utilities.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Commands.IssueResellerInvitationLink;

/// <summary>
/// Behavioural contract of <see cref="IssueResellerInvitationLinkCommandHandler"/>: a link is issued
/// only for a reseller login that has not been claimed, it is re-minted rather than recovered, the
/// previous link stops working, and every attempt, granted or refused, is journalled without the link.
/// </summary>
public sealed class IssueResellerInvitationLinkCommandHandlerTests : IDisposable
{
    /// <summary>The reseller whose login is invited.</summary>
    private static readonly Guid ResellerId = Guid.Parse("cccccccc-0000-4000-8000-000000000001");

    /// <summary>The fixed instant the clock reads.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The module's context, in memory.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

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
    private IssueResellerInvitationLinkCommandHandler NewHandler()
    {
        return new IssueResellerInvitationLinkCommandHandler(
            new ResellerInvitationIssuer(
                _context,
                _resellers,
                new InvitationIssuer(_context, new StubAccountDirectory(), new AccountOwnerFinder(_context))),
            new InvitationLinkBuilder(
                new OptionsWrapper<PanelOptions>(new PanelOptions { PublicUrl = "https://panel.example.com" })),
            new IdentityAuditJournal(_audit, _admin),
            _clock,
            _admin);
    }

    /// <summary>Builds the request for the reseller under test.</summary>
    private static IssueResellerInvitationLinkCommand Command()
    {
        return new IssueResellerInvitationLinkCommand(ResellerId, "203.0.113.7", "unit-tests");
    }

    /// <summary>Stores the reseller's invited login, with a token whose plaintext the test knows.</summary>
    /// <param name="state">Whether the reseller is in service.</param>
    private async Task<User> SeedInvitedResellerLoginAsync(ResellerState state = ResellerState.Active)
    {
        _resellers.Add(ResellerId, "reseller-one", state);
        var user = User.InviteReseller(Guid.NewGuid(), "reseller-one", "reseller@example.com", ResellerId, Now);
        _context.Users.Add(user);
        _context.InvitationTokens.Add(
            new InvitationToken(Guid.NewGuid(), user.Id, InvitationTokenHasher.Hash("the-mailed-token"), Now));
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

    /// <summary>A pending login yields a link whose token is the one now stored as a digest, replacing the mailed one.</summary>
    [Fact]
    public async Task A_pending_reseller_login_yields_a_link_that_replaces_the_mailed_token()
    {
        await SeedInvitedResellerLoginAsync();

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var link = result.Value.Link.Reveal();
        Assert.StartsWith("https://panel.example.com/accept-invitation?token=", link, StringComparison.Ordinal);
        var live = Assert.Single(await _context.InvitationTokens.Where(token => token.UsedAt == null).ToListAsync());
        Assert.Equal(InvitationTokenHasher.Hash(TokenIn(link)), live.TokenHash);
        Assert.NotEqual(InvitationTokenHasher.Hash("the-mailed-token"), live.TokenHash);
        Assert.Equal(Now + InvitationToken.Lifetime, result.Value.ExpiresAt);
    }

    /// <summary>The granted attempt is journalled under the administrator, naming the login and carrying no link.</summary>
    [Fact]
    public async Task A_granted_link_is_journalled_under_the_administrator_without_the_link()
    {
        await SeedInvitedResellerLoginAsync();

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.InvitationLinkIssued, entry.Action);
        Assert.True(entry.Succeeded);
        Assert.Equal(_admin.UserId, entry.ActorUserId);
        Assert.DoesNotContain(TokenIn(result.Value.Link.Reveal()), entry.ToString());
    }

    /// <summary>A reseller with no login gets it rebuilt and a link to it, journalled under the administrator.</summary>
    [Fact]
    public async Task A_reseller_with_no_login_gets_it_rebuilt_and_a_link()
    {
        _resellers.Add(ResellerId, "reseller-one", contactEmail: "contact@example.com");

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var login = Assert.Single(await _context.Users.ToListAsync());
        Assert.Equal(ResellerId, login.ResellerId);
        Assert.Equal("contact@example.com", login.Email);
        var live = Assert.Single(await _context.InvitationTokens.Where(token => token.UsedAt == null).ToListAsync());
        Assert.Equal(login.Id, live.UserId);
        Assert.Equal(InvitationTokenHasher.Hash(TokenIn(result.Value.Link.Reveal())), live.TokenHash);
        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.InvitationLinkIssued, entry.Action);
        Assert.Equal(_admin.UserId, entry.ActorUserId);
    }

    /// <summary>An unknown reseller is refused and the probe is journalled.</summary>
    [Fact]
    public async Task A_link_for_an_unknown_reseller_is_refused_and_the_probe_is_journalled()
    {
        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.Equal("InvitationResellerNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.False(Assert.Single(_audit.Written).Succeeded);
    }

    /// <summary>A login that already has a password gets no link and keeps its state.</summary>
    [Fact]
    public async Task A_reseller_login_that_has_a_password_gets_no_link()
    {
        var login = await SeedInvitedResellerLoginAsync();
        login.Activate("hash");
        await _context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.Equal("InvitationLoginNotPending", result.Error!.Code);
        Assert.False(Assert.Single(_audit.Written).Succeeded);
        Assert.Single(await _context.InvitationTokens.ToListAsync());
    }

    /// <summary>A suspended reseller gets no link and keeps its outstanding token.</summary>
    [Fact]
    public async Task A_suspended_reseller_gets_no_link_and_keeps_its_token()
    {
        await SeedInvitedResellerLoginAsync(ResellerState.Suspended);

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.Equal("InvitationResellerSuspended", result.Error!.Code);
        Assert.Null(Assert.Single(await _context.InvitationTokens.ToListAsync()).UsedAt);
    }
}
