namespace Maran.Modules.Identity.Domain.Entities;

/// <summary>
/// The open counting window for one source address: when it started and how many sign-ins have been
/// refused inside it. One row per address that has failed recently, never one per failure.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a table and not a dictionary in memory.</b> The count has to survive the requests
/// it spans, and an in-process map does three things wrong at once: it is lost on every restart —
/// so restarting the panel is a free reset for whoever is attacking it — it grows with the number of
/// distinct addresses seen rather than with the number currently attacking, and nothing evicts the
/// entries of an address that never comes back. A row has a durable count, is removed the moment its
/// window is announced as an attack, is reclaimed by <c>BruteForceDetector</c> once it has closed
/// unannounced, and costs one indexed statement per refused sign-in — beside an Argon2id
/// verification that costs 64 MiB and three passes, and an audit INSERT that already happens on the
/// same path.
/// </para>
/// <para>
/// <b>A FIXED window, not a rolling one.</b> The row remembers when counting started and the count
/// restarts wholesale once that much time has passed. A rolling window would need the timestamp of
/// every individual failure — that is the row-per-failure shape this type exists to avoid — and buys
/// nothing here: the difference between the two is at most one window's worth of patience, and the
/// window is the setting an operator tunes.
/// </para>
/// <para>
/// <b>The address is the key.</b> There is exactly one open window per address, so a detection
/// cannot be dodged by rotating usernames, and two spellings of one address cannot become two
/// counters — the caller normalises before it ever gets here (<c>ClientAddress</c>).
/// </para>
/// </remarks>
public sealed class FailedLoginByIp
{
    /// <summary>The source address, in the canonical spelling <c>ClientAddress</c> produces.</summary>
    public string IpAddress { get; private set; }

    /// <summary>
    /// When the open window began. Paired with the address it identifies a detection, so that a
    /// message delivered twice extends no ban and counts as no second offence.
    /// </summary>
    public DateTimeOffset WindowStart { get; private set; }

    /// <summary>How many sign-ins this address has had refused since <see cref="WindowStart"/>.</summary>
    public int Failures { get; private set; }

    /// <summary>Opens a window for an address whose first failure has just been refused.</summary>
    /// <param name="ipAddress">The source address, already normalised.</param>
    /// <param name="at">The instant of that first failure, taken from <see cref="IClock"/>.</param>
    public FailedLoginByIp(string ipAddress, DateTimeOffset at)
    {
        IpAddress = ipAddress;
        WindowStart = at;
        Failures = 1;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private FailedLoginByIp()
    {
        IpAddress = string.Empty;
    }

    /// <summary>Counts one more refused sign-in, starting a fresh window when the old one has closed.</summary>
    /// <param name="at">The instant of the attempt, taken from <see cref="IClock"/>.</param>
    /// <param name="window">How long a window lasts, from the module's brute-force policy.</param>
    public void RecordFailure(DateTimeOffset at, TimeSpan window)
    {
        if (at - WindowStart >= window)
        {
            // The window closed while nobody was looking. Reusing the row rather than deleting and
            // re-inserting it is what keeps a returning address to one row for the life of the panel.
            WindowStart = at;
            Failures = 1;
            return;
        }

        Failures++;
    }

    /// <summary>Whether this window has reached the count that makes it an attack.</summary>
    /// <param name="threshold">The policy's failures-per-address, which is never zero or negative.</param>
    /// <returns>True once the address has failed at least <paramref name="threshold"/> times.</returns>
    public bool HasReached(int threshold)
    {
        return Failures >= threshold;
    }
}
