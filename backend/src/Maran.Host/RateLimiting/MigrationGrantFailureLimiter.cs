using System.Collections.Concurrent;
using Maran.Host.Configuration;
using Microsoft.Extensions.Options;

namespace Maran.Host.RateLimiting;

/// <summary>
/// Counts export-grant authentication FAILURES per client address and locks an address out once it has made too
/// many, so a flood of wrong tokens cannot buy an unauthenticated database lookup and hash per request
/// (issue #127).
/// </summary>
/// <remarks>
/// <para>
/// <b>The same reason, the same numbers, a table of its own.</b> Authorization runs before the rate limiter, so a
/// refused grant is answered before any limiter sees it; the count has to live where the failure is decided, which
/// is the grant handler. It reads the sign-in numbers (<see cref="RateLimitOptions.LoginMaxAttempts"/> and
/// <see cref="RateLimitOptions.LoginLockoutSeconds"/>) exactly as <see cref="ApiKeyFailureLimiter"/> does, as a
/// fixed window from the first failure.
/// </para>
/// <para>
/// <b>Why it is not <see cref="ApiKeyFailureLimiter"/>.</b> The two doors have different callers. A reseller's
/// integration behind a shared address must not be locked out of its API key because somebody guessed at a grant
/// from that address, and an administrator pulling a package must not be locked out because an integration
/// presented a stale key. Two singletons keep two budgets. Only failures count: a live grant never spends it.
/// </para>
/// <para>
/// The table is bounded (<see cref="MaxTrackedAddresses"/>). When it is full of live windows a new address is not
/// recorded, which means an attacker holding that many addresses is not limited by this class; each of them still
/// costs the attacker one request per failure, and the token is 256 random bits.
/// </para>
/// </remarks>
public sealed class MigrationGrantFailureLimiter
{
    /// <summary>The most addresses with a live failure window held at once.</summary>
    private const int MaxTrackedAddresses = 50_000;

    /// <summary>How often a full table is swept for expired windows, in seconds.</summary>
    private const int SweepIntervalSeconds = 5;

    /// <summary>The failure windows, by client address in the panel's canonical spelling.</summary>
    private readonly ConcurrentDictionary<string, FailureWindow> _windows = new(StringComparer.Ordinal);

    /// <summary>The configured attempt count and window.</summary>
    private readonly RateLimitOptions _options;

    /// <summary>The ticks of the earliest moment the table may be swept again.</summary>
    private long _nextSweepTicks;

    /// <summary>Creates the limiter.</summary>
    /// <param name="options">The panel's rate-limit settings; the sign-in numbers are the ones used.</param>
    public MigrationGrantFailureLimiter(IOptions<RateLimitOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Says whether an address has used up its failures and is refused for now.</summary>
    /// <param name="address">The client address, spelled by <c>ClientAddress</c>.</param>
    /// <param name="now">The current time.</param>
    /// <returns><c>true</c> when the address made too many failures within the window.</returns>
    public bool IsLockedOut(string address, DateTimeOffset now)
    {
        if (!_windows.TryGetValue(address, out var window))
        {
            return false;
        }

        return !window.HasExpired(now, LockoutWindow()) && window.Count >= _options.LoginMaxAttempts;
    }

    /// <summary>Counts one failed authentication against an address.</summary>
    /// <param name="address">The client address, spelled by <c>ClientAddress</c>.</param>
    /// <param name="now">The current time.</param>
    public void RecordFailure(string address, DateTimeOffset now)
    {
        var window = LockoutWindow();

        if (!_windows.ContainsKey(address) && _windows.Count >= MaxTrackedAddresses)
        {
            Sweep(now, window);
            if (_windows.Count >= MaxTrackedAddresses)
            {
                return;
            }
        }

        _windows.AddOrUpdate(
            address,
            _ =>
            {
                return new FailureWindow(1, now);
            },
            (_, current) =>
            {
                return current.HasExpired(now, window) ? new FailureWindow(1, now) : current.WithOneMoreFailure();
            });
    }

    /// <summary>The length of the window failures are counted over.</summary>
    /// <returns>The configured lockout duration.</returns>
    private TimeSpan LockoutWindow()
    {
        return TimeSpan.FromSeconds(_options.LoginLockoutSeconds);
    }

    /// <summary>Removes expired windows, at most once per <see cref="SweepIntervalSeconds"/>.</summary>
    /// <param name="now">The current time.</param>
    /// <param name="window">The length of a failure window.</param>
    private void Sweep(DateTimeOffset now, TimeSpan window)
    {
        var next = Interlocked.Read(ref _nextSweepTicks);
        if (now.UtcTicks < next
            || Interlocked.CompareExchange(
                ref _nextSweepTicks,
                now.UtcTicks + TimeSpan.FromSeconds(SweepIntervalSeconds).Ticks,
                next) != next)
        {
            return;
        }

        foreach (var entry in _windows.Where(candidate =>
        {
            return candidate.Value.HasExpired(now, window);
        }))
        {
            _windows.TryRemove(entry);
        }
    }

    /// <summary>How many failures one address made since its window began.</summary>
    /// <param name="Count">The failures counted in the window.</param>
    /// <param name="Start">When the first failure of the window happened.</param>
    private readonly record struct FailureWindow(int Count, DateTimeOffset Start)
    {
        /// <summary>Whether the window has run its course and the address starts afresh.</summary>
        /// <param name="now">The current time.</param>
        /// <param name="length">The length of a failure window.</param>
        /// <returns><c>true</c> when <paramref name="length"/> has passed since <see cref="Start"/>.</returns>
        public bool HasExpired(DateTimeOffset now, TimeSpan length)
        {
            return now - Start >= length;
        }

        /// <summary>The same window with one more failure counted.</summary>
        /// <returns>The updated window.</returns>
        public FailureWindow WithOneMoreFailure()
        {
            return this with { Count = Count + 1 };
        }
    }
}
