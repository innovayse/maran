using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Seeders;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Seeders;

/// <summary>
/// Starting the clock on the installer's one-time setup token: when the panel records the token's
/// age, and when it must not.
/// </summary>
public sealed class SetupTokenWindowSeederTests : IDisposable
{
    /// <summary>The token every test here configures.</summary>
    private const string Token = "a-one-time-token";

    /// <summary>The instant the panel starts in these tests.</summary>
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);

    /// <summary>The module's database for this test.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>The clock the window is stamped from; movable, so a restart can happen later.</summary>
    private readonly FakeClock _clock = new(Now);

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>The first start of an unclaimed panel records when it saw the token.</summary>
    /// <remarks>
    /// The measurement the expiry is counted from, and the only one the panel can take for itself:
    /// the installer writes the token with no issue time beside it.
    /// </remarks>
    [Fact]
    public async Task The_first_start_of_an_unclaimed_panel_records_when_it_saw_the_token()
    {
        await NewSeeder().SeedAsync(CancellationToken.None);

        var window = await _context.SetupTokenWindows.SingleAsync();
        Assert.Equal(Now, window.OpenedAt);
    }

    /// <summary>Restarting the panel does not extend a window that is already open.</summary>
    /// <remarks>
    /// The property that makes the expiry mean anything at all. If a restart re-stamped the row, a
    /// panel left running and rebooted — or one whose service is restarted nightly — would keep its
    /// token alive for ever, which is the state the expiry exists to end.
    /// </remarks>
    [Fact]
    public async Task Restarting_the_panel_does_not_extend_a_window_that_is_already_open()
    {
        await NewSeeder().SeedAsync(CancellationToken.None);

        _clock.Advance(TimeSpan.FromHours(10));
        await NewSeeder().SeedAsync(CancellationToken.None);

        var window = await _context.SetupTokenWindows.SingleAsync();
        Assert.Equal(Now, window.OpenedAt);
    }

    /// <summary>A token the operator has replaced starts a fresh window.</summary>
    /// <remarks>
    /// The recovery path for an install whose token expired, exercised through the seeder because
    /// that is where an operator's restart lands. The instant must MOVE, which is what distinguishes
    /// this from the restart above.
    /// </remarks>
    [Fact]
    public async Task A_token_the_operator_has_replaced_starts_a_fresh_window()
    {
        await NewSeeder().SeedAsync(CancellationToken.None);

        _clock.Advance(TimeSpan.FromDays(3));
        await NewSeeder(configuredToken: "a-replacement-token").SeedAsync(CancellationToken.None);

        var window = await _context.SetupTokenWindows.SingleAsync();
        Assert.Equal(Now + TimeSpan.FromDays(3), window.OpenedAt);
    }

    /// <summary>A panel that already has an administrator records no window.</summary>
    /// <remarks>
    /// Once a user exists the token is worth nothing whatever its age — the handler refuses on that
    /// alone — so a row here would be the recorded age of something already dead.
    /// </remarks>
    [Fact]
    public async Task A_panel_that_already_has_an_administrator_records_no_window()
    {
        _context.Users.Add(new User(Guid.NewGuid(), "someone", "s@example.com", "hash", UserRole.Admin, Now));
        await _context.SaveChangesAsync();

        await NewSeeder().SeedAsync(CancellationToken.None);

        Assert.Empty(await _context.SetupTokenWindows.ToListAsync());
    }

    /// <summary>A panel with no configured token records no window.</summary>
    /// <remarks>
    /// The ordinary state of every panel whose setup is finished and whose operator has cleared the
    /// value. There is nothing to time, and a row would claim a window over a token that does not
    /// exist.
    /// </remarks>
    [Fact]
    public async Task A_panel_with_no_configured_token_records_no_window()
    {
        await NewSeeder(configuredToken: string.Empty).SeedAsync(CancellationToken.None);

        Assert.Empty(await _context.SetupTokenWindows.ToListAsync());
    }

    /// <summary>Builds the seeder over this test's database and clock.</summary>
    /// <param name="configuredToken">The token the installer is taken to have written.</param>
    /// <returns>The seeder under test.</returns>
    private SetupTokenWindowSeeder NewSeeder(string configuredToken = Token)
    {
        return new SetupTokenWindowSeeder(
            _context,
            new SetupTokenWindowKeeper(_context, _clock),
            new OptionsWrapper<SetupOptions>(new SetupOptions { Token = configuredToken }),
            NullLogger<SetupTokenWindowSeeder>.Instance);
    }
}
