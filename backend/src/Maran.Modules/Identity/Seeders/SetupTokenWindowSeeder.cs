using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Seeders;

/// <summary>
/// Starts the clock on the installer's one-time setup token at the panel's first start, so that an
/// install nobody finishes stops being claimable.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the clock cannot start anywhere else.</b> The installer writes the token and no issue time
/// beside it, and this module may not read the file. The first instant the panel can honestly record
/// is the first time it ran with that token configured — which is the install itself, seconds after
/// the token was generated. So the seed is not reference data: it is a MEASUREMENT, taken at the only
/// moment it can be taken.
/// </para>
/// <para>
/// <b>It runs only while the panel has no administrator.</b> Once a user exists the token is worth
/// nothing whatever its age, and writing a row for it then would be recording the age of something
/// already dead.
/// </para>
/// <para>
/// <b>Re-running it is harmless and is the point.</b> <see cref="SetupTokenWindowKeeper.OpenAsync"/>
/// opens a window only when there is none or when the token has changed, so every restart of an
/// unfinished install leaves the original instant standing — a panel could not be kept claimable by
/// restarting it — while a token the operator has replaced starts a fresh window, which is how an
/// operator recovers an install whose token expired.
/// </para>
/// </remarks>
public sealed class SetupTokenWindowSeeder
{
    /// <summary>Pre-compiled log delegate for a window this start opened.</summary>
    /// <remarks>
    /// It names neither the token nor its digest. What an operator needs from the log is that the
    /// token now has an expiry and when it began, and a digest in a log file is a value an attacker
    /// can test candidate tokens against offline.
    /// </remarks>
    private static readonly Action<ILogger, DateTimeOffset, Exception?> LogWindowOpen =
        LoggerMessage.Define<DateTimeOffset>(
            LogLevel.Information,
            new EventId(1, nameof(SetupTokenWindowSeeder)),
            "The one-time setup token is usable until {ExpiresAt}; after that a new token must be "
            + "configured before this panel can be claimed");

    /// <summary>The module's database context, asked whether the panel already has an administrator.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The authority on the token's window.</summary>
    private readonly SetupTokenWindowKeeper _keeper;

    /// <summary>The configured token, empty on a panel whose setup is finished.</summary>
    private readonly string _configuredToken;

    /// <summary>Where the window's end is reported, for an operator reading the panel's own log.</summary>
    private readonly ILogger<SetupTokenWindowSeeder> _logger;

    /// <summary>Creates the seeder.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="keeper">The authority on the token's window.</param>
    /// <param name="setupOptions">The bound <see cref="SetupOptions"/>, carrying the installer's token.</param>
    /// <param name="logger">Where the window's end is reported.</param>
    public SetupTokenWindowSeeder(
        IdentityDbContext dbContext,
        SetupTokenWindowKeeper keeper,
        IOptions<SetupOptions> setupOptions,
        ILogger<SetupTokenWindowSeeder> logger)
    {
        _dbContext = dbContext;
        _keeper = keeper;
        _configuredToken = setupOptions.Value.Token;
        _logger = logger;
    }

    /// <summary>Opens the token's window, unless there is no token or the panel already has an owner.</summary>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_configuredToken))
        {
            return;
        }

        if (await _dbContext.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var openedAt = await _keeper.OpenAsync(_configuredToken, cancellationToken);

        LogWindowOpen(_logger, openedAt + SetupTokenWindowKeeper.Window, null);
    }
}
