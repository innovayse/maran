using Maran.Host.Demo.Guard.Enums;
using Maran.Host.Demo.Options;
using Microsoft.Extensions.Options;

namespace Maran.Host.Demo.Guard;

/// <summary>
/// Bounds how fast sign-ins, refreshes and sign-outs can grow the session and audit tables: one allowance
/// per pseudonymous visitor and one for the whole demo, each per time window. Thread-safe and bounded.
/// </summary>
/// <remarks>
/// <para>
/// <b>Apart from the mutation budget on purpose.</b> Getting in and out does not spend
/// <see cref="DemoMutationBudget"/>, so a script cannot spend the demo's changes to lock newcomers out of
/// signing in. It is not free either: every sign-in and refresh writes a session row and an audit row, so
/// an unmetered endpoint is an unbounded table (threat note T6).
/// </para>
/// <para>
/// <b>Both allowances renew each window</b>, so exhausting the global one costs the demo a window, not the
/// rest of the reset interval. A visitor is a network (see <see cref="Pseudonymisation.VisitorNetwork"/>),
/// so the per-visitor allowance bounds a subscriber, not an address.
/// </para>
/// </remarks>
public sealed class DemoSignInBudget
{
    /// <summary>Guards every field below.</summary>
    private readonly object _gate = new();

    /// <summary>The visitors' windows by pseudonymous key.</summary>
    private readonly DemoVisitorWindows _visitors;

    /// <summary>The whole demo's window.</summary>
    private readonly DemoVisitorWindow _global;

    /// <summary>The settings.</summary>
    private readonly DemoOptions _options;

    /// <summary>The length of every window.</summary>
    private readonly TimeSpan _window;

    /// <summary>Creates the budget.</summary>
    /// <param name="options">The settings.</param>
    /// <param name="clock">The time source, for the first global window.</param>
    public DemoSignInBudget(IOptions<DemoOptions> options, IClock clock)
    {
        _options = options.Value;
        _window = TimeSpan.FromSeconds(_options.VisitorWindowSeconds);
        _visitors = new DemoVisitorWindows(_options.MaxTrackedVisitors, _window);
        _global = new DemoVisitorWindow(clock.UtcNow);
    }

    /// <summary>Tries to take one sign-in, refresh or sign-out for a visitor.</summary>
    /// <param name="visitorKey">The visitor's pseudonymous address.</param>
    /// <param name="now">The current instant, from the clock.</param>
    /// <param name="retryAfter">When refused, how long until the window that refused ends.</param>
    /// <returns>Whether the call may run, and if not whose allowance was spent.</returns>
    public DemoBudgetDecision TryTake(string visitorKey, DateTimeOffset now, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;

        lock (_gate)
        {
            var visitor = _visitors.Current(visitorKey, now);
            if (visitor.Count >= _options.VisitorSignInsPerWindow)
            {
                retryAfter = visitor.TimeLeft(now, _window);
                return DemoBudgetDecision.VisitorRateExceeded;
            }

            if (_global.HasEnded(now, _window))
            {
                _global.Restart(now);
            }

            if (_global.Count >= _options.GlobalSignInsPerWindow)
            {
                retryAfter = _global.TimeLeft(now, _window);
                return DemoBudgetDecision.GlobalExhausted;
            }

            visitor.CountOne();
            _global.CountOne();
            return DemoBudgetDecision.Granted;
        }
    }
}
