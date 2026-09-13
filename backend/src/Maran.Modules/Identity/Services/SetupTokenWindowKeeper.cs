using System.Security.Cryptography;
using System.Text;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// The authority on how long the installer's one-time setup token still works: it opens the token's
/// window when the panel first observes it, and answers whether that window has closed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The window is twenty-four hours, and the argument is a shape rather than a number.</b> An
/// install is finished in minutes, so every length is generous for the ordinary case and the choice
/// is entirely about the two failures either side of it. Too short strands the operator who installs
/// the panel and is then called away before creating the administrator: they come back to a server
/// they cannot claim, which turns a product decision into a support call and teaches them to keep the
/// token somewhere convenient — the exact habit the token's handling is designed to discourage. Too
/// long is the state
/// <c>docs/superpowers/notes/2026-09-11-setup-token-in-a-url-threat-note.md</c> calls dangerous — its
/// check 6, which this type closes: the token remains permission to own the server
/// while nobody is watching the server. A day is the longest span for which "the person who ran the
/// installer is still the person coming back to it" is a safe assumption: it survives a lunch, a
/// meeting, an evening and a night's sleep, and it does not survive a weekend, a holiday or a
/// forgotten trial install — none of which should still be claimable.
/// </para>
/// <para>
/// <b>Where the clock comes from.</b> <c>IClock</c>, injected, never the ambient clock — which is a
/// banned symbol in this repository (rules/README.md "Banned APIs") precisely so that a window like
/// this one can be tested at both sides of its boundary instead of being believed.
/// </para>
/// <para>
/// <b>What the window is measured FROM, and why it is not the first attempt.</b> It is the first
/// moment this panel ran with that token configured, recorded by a startup seeder. Measuring from the
/// first attempt to USE the token would leave the abandoned install — the case the expiry exists for
/// — with a clock that never starts, because nobody ever attempts anything.
/// </para>
/// <para>
/// <b>Opening is idempotent and self-healing.</b> <see cref="OpenAsync"/> writes the row only when
/// there is none, or when the configured token is a different one from the one the row was opened
/// for. The handler calls it too, so a panel whose seeder never ran — an upgrade whose first request
/// arrives before any restart, a database restored without this table's row — has its window opened
/// by the first attempt rather than being refused outright. That is a deliberate softness: a refusal
/// there would strand an operator over a missing row, and an attacker who can delete rows from the
/// panel's database does not need a setup token.
/// </para>
/// </remarks>
public sealed class SetupTokenWindowKeeper
{
    /// <summary>How long the installer's token works, counted from the panel's first sight of it.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The panel's clock; never the ambient one.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the keeper.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    public SetupTokenWindowKeeper(IdentityDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    /// <summary>Opens this token's window if it is not open already, and reports when it opened.</summary>
    /// <param name="token">The configured token; only its digest is stored.</param>
    /// <param name="cancellationToken">Cancels the read and any write.</param>
    /// <returns>The instant the window for this token opened.</returns>
    public async Task<DateTimeOffset> OpenAsync(string token, CancellationToken cancellationToken)
    {
        var fingerprint = Fingerprint(token);

        var window = await _dbContext.SetupTokenWindows
            .SingleOrDefaultAsync(row => row.Id == SetupTokenWindow.SingletonId, cancellationToken);

        if (window is null)
        {
            window = new SetupTokenWindow(fingerprint, _clock.UtcNow);
            _dbContext.SetupTokenWindows.Add(window);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return window.OpenedAt;
        }

        if (!string.Equals(window.TokenFingerprint, fingerprint, StringComparison.Ordinal))
        {
            // A different token is configured than the one this window was opened for, so the
            // operator has replaced it — which is how an expired install is recovered. The clock
            // starts again from now.
            window.Reopen(fingerprint, _clock.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return window.OpenedAt;
    }

    /// <summary>Whether this token's window has already closed.</summary>
    /// <param name="token">The configured token; only its digest is read.</param>
    /// <param name="cancellationToken">Cancels the read and any write.</param>
    /// <returns>True when the token is older than <see cref="Window"/> and may no longer be used.</returns>
    /// <remarks>
    /// The comparison is inclusive of the boundary instant: a token whose window is exactly
    /// <see cref="Window"/> old has expired. Either side would be defensible for one instant; what
    /// matters is that the boundary is stated somewhere a test can sit on both sides of it.
    /// </remarks>
    public async Task<bool> HasExpiredAsync(string token, CancellationToken cancellationToken)
    {
        var openedAt = await OpenAsync(token, cancellationToken);

        return _clock.UtcNow >= openedAt + Window;
    }

    /// <summary>Digests a token so the panel can recognise it without storing it.</summary>
    /// <param name="token">The configured token.</param>
    /// <returns>Lowercase hex SHA-256 of the token's UTF-8 bytes.</returns>
    /// <remarks>
    /// SHA-256 rather than a password hash: this is not a credential being verified against a guess
    /// but a 192-bit random value being recognised, so there is nothing to slow an attacker down
    /// about — and a deliberately slow hash here would be run on every panel start.
    /// </remarks>
    private static string Fingerprint(string token)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
