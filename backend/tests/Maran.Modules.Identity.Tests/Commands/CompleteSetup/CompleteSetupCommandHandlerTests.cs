using System.Security.Cryptography;
using System.Text;
using Maran.Modules.Identity.Commands.CompleteSetup;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Commands.CompleteSetup;
/// <summary>Behavioural contract of complete setup command handler.</summary>

public sealed class CompleteSetupCommandHandlerTests : IDisposable
{
    private const string Token = "a-one-time-token";

    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private static CompleteSetupCommand Command(string token = Token)
    {
        return new CompleteSetupCommand(
            token, "admin", "admin@example.com", "correct horse battery staple", "203.0.113.7", "agent");
    }

    private CompleteSetupCommandHandler NewHandler(string configuredToken = Token)
    {
        var clock = new FakeClock(Now);

        return new CompleteSetupCommandHandler(
            _context,
            new Argon2idPasswordHasher(),
            new IdentityAuditJournal(_audit, new StubCurrentUser()),
            new SetupTokenWindowKeeper(_context, clock),
            clock,
            new OptionsWrapper<SetupOptions>(new SetupOptions { Token = configuredToken }));
    }

    /// <summary>Records a window that opened <paramref name="age"/> before the tests' fixed now.</summary>
    /// <param name="age">How long ago the panel first observed the token.</param>
    /// <param name="fingerprint">
    /// The digest the window was opened for. The default is a digest of nothing in particular, which
    /// is only usable by the tests that WANT a mismatch; the ones about the boundary pass the real
    /// token's digest.
    /// </param>
    private async Task OpenWindowAsync(TimeSpan age, string? fingerprint = null)
    {
        _context.SetupTokenWindows.Add(new SetupTokenWindow(fingerprint ?? new string('0', 64), Now - age));
        await _context.SaveChangesAsync();
    }

    /// <summary>The digest the keeper recognises a token by.</summary>
    /// <param name="token">The token to digest.</param>
    /// <returns>Lowercase hex SHA-256 of its UTF-8 bytes.</returns>
    private static string Fingerprint(string token)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>Completing setup on an empty panel creates an administrator.</summary>
    [Fact]
    public async Task Completing_setup_on_an_empty_panel_creates_an_administrator()
    {
        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var user = await _context.Users.SingleAsync();
        Assert.Equal("admin", user.Username);
        Assert.Equal(UserRole.Admin, user.Role);
    }

    /// <summary>The created administrators password is stored only as a hash.</summary>
    [Fact]
    public async Task The_created_administrators_password_is_stored_only_as_a_hash()
    {
        await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var stored = (await _context.Users.SingleAsync()).PasswordHash;
        Assert.DoesNotContain("correct horse battery staple", stored, StringComparison.Ordinal);
        Assert.StartsWith("$argon2id$", stored, StringComparison.Ordinal);
    }

    /// <summary>Completing setup with a wrong token fails and creates nobody.</summary>
    [Fact]
    public async Task Completing_setup_with_a_wrong_token_fails_and_creates_nobody()
    {
        var result = await NewHandler().HandleAsync(Command(token: "not-the-token"), CancellationToken.None);

        Assert.Equal("SetupTokenInvalidUnauthorized", result.Error!.Code);
        Assert.Empty(await _context.Users.ToListAsync());
    }

    /// <summary>Setup is refused when no token is configured at all.</summary>
    [Fact]
    public async Task Setup_is_refused_when_no_token_is_configured_at_all()
    {
        // An empty configured token must not be satisfied by an empty supplied one: that would
        // hand the panel to the first stranger to post an empty string.
        var result = await NewHandler(configuredToken: string.Empty).HandleAsync(Command(token: ""), CancellationToken.None);

        Assert.Equal("SetupTokenInvalidUnauthorized", result.Error!.Code);
    }

    /// <summary>Completing setup when a user already exists is refused even with the right token.</summary>
    [Fact]
    public async Task Completing_setup_when_a_user_already_exists_is_refused_even_with_the_right_token()
    {
        _context.Users.Add(new User(Guid.NewGuid(), "someone", "s@example.com", "hash", UserRole.Admin, Now));
        await _context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.Equal("SetupAlreadyCompletedForbidden", result.Error!.Code);
        Assert.Single(await _context.Users.ToListAsync());
    }

    /// <summary>Completing setup writes an audit event that does not contain the token or the password.</summary>
    [Fact]
    public async Task Completing_setup_writes_an_audit_event_that_does_not_contain_the_token_or_the_password()
    {
        await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var entry = _audit.Written.Single();
        Assert.Equal(AuditActions.AdministratorCreated, entry.Action);
        Assert.DoesNotContain(Token, entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("correct horse", entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>A refused setup writes no audit entry naming a user that was never created.</summary>
    [Fact]
    public async Task A_refused_setup_writes_no_audit_entry_naming_a_user_that_was_never_created()
    {
        await NewHandler().HandleAsync(Command(token: "not-the-token"), CancellationToken.None);

        Assert.Empty(_audit.Written);
    }

    /// <summary>A token whose window has closed is refused and creates nobody.</summary>
    /// <remarks>
    /// The defect this closes: until the window existed, an install nobody finished left a live token
    /// — permission to own the whole server — for as long as the file holding it existed. The second
    /// assertion is the one that matters: a refusal that still created the administrator would be no
    /// refusal at all.
    /// </remarks>
    [Fact]
    public async Task A_token_whose_window_has_closed_is_refused_and_creates_nobody()
    {
        await OpenWindowAsync(SetupTokenWindowKeeper.Window + TimeSpan.FromSeconds(1), Fingerprint(Token));

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.Equal("SetupTokenExpiredUnauthorized", result.Error!.Code);
        Assert.Empty(await _context.Users.ToListAsync());
    }

    /// <summary>A token in the last second of its window still creates the administrator.</summary>
    /// <remarks>
    /// The INVERSE CONTROL the refusal above owes (rules/testing.md): an expiry check mutated to
    /// refuse everything satisfies every test that only ever hands it an expired token, and the cost
    /// of that mutation shipping would be a server nobody can claim. The two tests sit one second
    /// apart on either side of the same boundary.
    /// </remarks>
    [Fact]
    public async Task A_token_in_the_last_second_of_its_window_still_creates_the_administrator()
    {
        await OpenWindowAsync(SetupTokenWindowKeeper.Window - TimeSpan.FromSeconds(1), Fingerprint(Token));

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(await _context.Users.ToListAsync());
    }

    /// <summary>A token exactly as old as its window is already refused.</summary>
    /// <remarks>
    /// The boundary asserted at the exact value rather than as a bound, because "expires after 24
    /// hours" and "expires at 24 hours" differ by one instant and only one of them is what the code
    /// says. The keeper documents this side as the chosen one.
    /// </remarks>
    [Fact]
    public async Task A_token_exactly_as_old_as_its_window_is_already_refused()
    {
        await OpenWindowAsync(SetupTokenWindowKeeper.Window, Fingerprint(Token));

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.Equal("SetupTokenExpiredUnauthorized", result.Error!.Code);
    }

    /// <summary>A token twenty-three hours old still works, and one twenty-five hours old does not.</summary>
    /// <remarks>
    /// <para>
    /// <b>These two offsets are ABSOLUTE on purpose, and that is the whole reason this test exists.</b>
    /// Every other test about the window expresses its offset relative to
    /// <c>SetupTokenWindowKeeper.Window</c> — <c>Window + 1s</c>, <c>Window - 1s</c>, <c>Window</c>
    /// exactly — so all of them move with the constant and none of them can see the constant CHANGE.
    /// Measured, not supposed: a mutation replacing <c>FromHours(24)</c> with <c>FromHours(48)</c>
    /// scored SURVIVED against the whole solution, 3013 passed / 0 failed, while a token thirty hours
    /// old went from refused to accepted. The boundary was tested; the NUMBER was held up by nothing.
    /// </para>
    /// <para>
    /// So the number is pinned here in hours the reader can check against the argument for it on
    /// <see cref="SetupTokenWindowKeeper"/> — a day, chosen as the longest span over which "the person
    /// who ran the installer is the person coming back to it" is safe. Twenty-three and twenty-five
    /// bracket it without sitting on the boundary instant, which is a different property with its own
    /// test. Changing the window is now a deliberate act that edits this test and reads that argument,
    /// rather than a one-token edit nothing notices.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_window_is_twenty_four_hours_so_a_token_is_live_at_twenty_three_and_dead_at_twenty_five()
    {
        await OpenWindowAsync(TimeSpan.FromHours(23), Fingerprint(Token));
        Assert.True((await NewHandler().HandleAsync(Command(), CancellationToken.None)).IsSuccess);

        // Removed through the tracker rather than with ExecuteDelete, which the InMemory provider
        // these tests run on does not implement. The panel must be back to "unclaimed, no window"
        // before the second half, or the first gate would answer it instead of the expiry.
        _context.Users.RemoveRange(await _context.Users.ToListAsync());
        _context.SetupTokenWindows.RemoveRange(await _context.SetupTokenWindows.ToListAsync());
        await _context.SaveChangesAsync();

        await OpenWindowAsync(TimeSpan.FromHours(25), Fingerprint(Token));
        var late = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.Equal("SetupTokenExpiredUnauthorized", late.Error!.Code);
        Assert.Empty(await _context.Users.ToListAsync());
    }

    /// <summary>A replaced token opens a new window, so an install whose token expired can still be claimed.</summary>
    /// <remarks>
    /// The other half of the expiry, and the half that keeps it from being a dead end: the operator
    /// configures a new token and this panel recognises it as a new one — the recorded fingerprint no
    /// longer matches — so the clock starts again. Without this the refusal above would make an
    /// abandoned install permanently unclaimable, which is a worse failure than the one it fixes.
    /// </remarks>
    [Fact]
    public async Task A_replaced_token_opens_a_new_window_so_an_expired_install_can_still_be_claimed()
    {
        await OpenWindowAsync(TimeSpan.FromDays(30));

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var window = await _context.SetupTokenWindows.SingleAsync();
        Assert.Equal(Now, window.OpenedAt);
        Assert.Equal(Fingerprint(Token), window.TokenFingerprint);
    }

    /// <summary>A panel with no recorded window opens one and lets that first attempt through.</summary>
    /// <remarks>
    /// The state an upgraded panel is in before its next restart, and a deliberate softness: refusing
    /// here would strand an operator over a missing row, while an attacker who can delete rows from
    /// the panel's database has no use for a setup token. The row is asserted as well as the success,
    /// because the point is that the clock has now started.
    /// </remarks>
    [Fact]
    public async Task A_panel_with_no_recorded_window_opens_one_and_lets_that_first_attempt_through()
    {
        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var window = await _context.SetupTokenWindows.SingleAsync();
        Assert.Equal(Now, window.OpenedAt);
    }

    /// <summary>An expired window does not tell a wrong token that it was otherwise acceptable.</summary>
    /// <remarks>
    /// The order of the two checks is itself a property: reporting the expiry to a caller who guessed
    /// wrong would confirm that the token they guessed was the configured one and only late. So a
    /// wrong token reads as a wrong token whatever the window says.
    /// </remarks>
    [Fact]
    public async Task An_expired_window_does_not_tell_a_wrong_token_that_it_was_otherwise_acceptable()
    {
        await OpenWindowAsync(SetupTokenWindowKeeper.Window + TimeSpan.FromDays(1), Fingerprint(Token));

        var result = await NewHandler().HandleAsync(Command(token: "not-the-token"), CancellationToken.None);

        Assert.Equal("SetupTokenInvalidUnauthorized", result.Error!.Code);
    }
}
