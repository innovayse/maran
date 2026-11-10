using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Models;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Remembers the last <see cref="LicenceCoverage"/> for a short time, so the request-time gate and the
/// module catalogue do not read two files and ask the agent for the machine id on every request. The
/// lifetime is measured by the injected clock, so a test drives it without sleeping.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a stale answer can cost.</b> Up to <see cref="TimeToLive"/> after a lease ends or a
/// licence expires, the cache may still say Covered. That is the price of not reading disk per request;
/// it is bounded to seconds against a lease that lives 14 days. Installing a licence calls
/// <see cref="Invalidate"/>, so the new verdict is seen at once.
/// </para>
/// <para>
/// A singleton by necessity: a scoped instance would remember nothing between requests. Concurrent
/// readers share one read (a gate on the refresh), so a burst after expiry costs one file read, not
/// one per request.
/// </para>
/// </remarks>
public sealed class LicenceCoverageCache : ILicenceCoverageInvalidator, IDisposable
{
    /// <summary>How long a read is trusted before the licence is read again.</summary>
    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(30);

    /// <summary>Reads the licence and lease when the remembered answer is missing or old.</summary>
    private readonly LicenceCoverageSource _source;

    /// <summary>The clock the lifetime is measured by.</summary>
    private readonly IClock _clock;

    /// <summary>Serialises refreshes so a burst of readers shares one read.</summary>
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    /// <summary>The remembered answer with the instant it was read, or null when none is held.</summary>
    private volatile Remembered? _remembered;

    /// <summary>Creates the cache.</summary>
    /// <param name="source">Reads the licence and lease.</param>
    /// <param name="clock">The clock the lifetime is measured by.</param>
    public LicenceCoverageCache(LicenceCoverageSource source, IClock clock)
    {
        _source = source;
        _clock = clock;
    }

    /// <summary>Returns the remembered coverage, reading it first when it is missing or older than the lifetime.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The licence coverage.</returns>
    public async Task<LicenceCoverage> GetAsync(CancellationToken cancellationToken)
    {
        if (IsFresh(_remembered))
        {
            return _remembered!.Coverage;
        }

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            // Another reader may have refreshed while this one waited for the gate.
            if (IsFresh(_remembered))
            {
                return _remembered!.Coverage;
            }

            var coverage = await _source.ReadAsync(cancellationToken);
            _remembered = new Remembered(coverage, _clock.UtcNow);

            return coverage;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <inheritdoc />
    public void Invalidate()
    {
        _remembered = null;
    }

    /// <summary>Releases the refresh gate.</summary>
    public void Dispose()
    {
        _refreshGate.Dispose();
    }

    /// <summary>Whether a remembered answer exists and is still inside its lifetime.</summary>
    /// <param name="remembered">The remembered answer, or null.</param>
    /// <returns>True when it may be used.</returns>
    private bool IsFresh(Remembered? remembered)
    {
        return remembered is not null && _clock.UtcNow - remembered.ReadAt < TimeToLive;
    }

    /// <summary>One remembered answer and when it was read.</summary>
    /// <param name="Coverage">The answer.</param>
    /// <param name="ReadAt">When it was read.</param>
    private sealed record Remembered(LicenceCoverage Coverage, DateTimeOffset ReadAt);
}
