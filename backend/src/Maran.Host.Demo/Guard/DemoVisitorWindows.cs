namespace Maran.Host.Demo.Guard;

/// <summary>
/// A bounded table of per-visitor windows, keyed by the visitor's pseudonymous address.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bounded.</b> At most <c>maxTracked</c> visitors are followed. When the table is full, expired windows
/// are dropped; if it is still full, a new visitor shares one overflow window, so the table cannot grow
/// without bound and no tracked visitor is evicted by a flood.
/// </para>
/// <para>
/// <b>The sweep is rationed.</b> Dropping expired windows walks the whole table, so it runs at most once
/// per window length. A flood of new keys against a full table then costs an O(1) lookup per request and
/// one O(n) walk per window, not a walk per request: the owner's lock is never held for a table scan on
/// the attacker's schedule. The price is that a window which expires just after a sweep keeps its slot until
/// the next one, and newcomers meanwhile share the overflow window, which is the safe direction.
/// </para>
/// <para>Not thread-safe; the owner locks around every call.</para>
/// </remarks>
public sealed class DemoVisitorWindows
{
    /// <summary>The key every visitor beyond the tracked ones shares.</summary>
    private const string OverflowKey = "overflow";

    /// <summary>The windows by key.</summary>
    private readonly Dictionary<string, DemoVisitorWindow> _windows = new(StringComparer.Ordinal);

    /// <summary>Visitors followed at once, the overflow window included.</summary>
    private readonly int _maxTracked;

    /// <summary>The length of every window.</summary>
    private readonly TimeSpan _length;

    /// <summary>When the table was last swept, or <c>null</c> if never.</summary>
    private DateTimeOffset? _lastSweep;

    /// <summary>Windows currently held, the overflow one included.</summary>
    public int Count
    {
        get { return _windows.Count; }
    }

    /// <summary>Creates the table.</summary>
    /// <param name="maxTracked">Visitors followed at once.</param>
    /// <param name="length">The length of every window.</param>
    public DemoVisitorWindows(int maxTracked, TimeSpan length)
    {
        _maxTracked = maxTracked;
        _length = length;
    }

    /// <summary>Finds a visitor's window, adding it within the bound, and starts a new one if it ended.</summary>
    /// <param name="key">The visitor's key.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>The visitor's current window, or the shared overflow one.</returns>
    public DemoVisitorWindow Current(string key, DateTimeOffset now)
    {
        if (_windows.TryGetValue(key, out var known))
        {
            return Rolled(known, now);
        }

        if (_windows.Count >= _maxTracked)
        {
            SweepOncePerWindow(now);
        }

        if (_windows.Count >= _maxTracked)
        {
            key = OverflowKey;
            if (_windows.TryGetValue(key, out var shared))
            {
                return Rolled(shared, now);
            }
        }

        var created = new DemoVisitorWindow(now);
        _windows[key] = created;
        return created;
    }

    /// <summary>Starts a new window when the held one has ended.</summary>
    /// <param name="window">The held window.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>The same window, restarted if it had ended.</returns>
    private DemoVisitorWindow Rolled(DemoVisitorWindow window, DateTimeOffset now)
    {
        if (window.HasEnded(now, _length))
        {
            window.Restart(now);
        }

        return window;
    }

    /// <summary>Drops the ended windows, unless a sweep already ran within the last window length.</summary>
    /// <param name="now">The current instant.</param>
    private void SweepOncePerWindow(DateTimeOffset now)
    {
        if (_lastSweep is { } last && now - last < _length)
        {
            return;
        }

        _lastSweep = now;
        foreach (var ended in _windows.Where(pair => { return pair.Value.HasEnded(now, _length); }).ToList())
        {
            _windows.Remove(ended.Key);
        }
    }
}
