using Maran.Host.Demo.Guard.Enums;
using Maran.Host.Demo.Options;
using Microsoft.Extensions.Options;

namespace Maran.Host.Demo.Guard;

/// <summary>
/// Spends the demo's mutation budgets: one for the whole reset window and one per pseudonymous visitor
/// per time window. Thread-safe and bounded.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order.</b> The visitor's window is asked first and a refusal there spends nothing, so a visitor
/// hammering the demo cannot burn the global budget with calls that were never going to run.
/// </para>
/// <para>
/// <b>Bounded.</b> The visitors live in a <see cref="DemoVisitorWindows"/> table: bounded, with its sweep
/// of expired windows rationed to one per window so a flood of new keys never walks the table under this
/// type's lock on every request.
/// </para>
/// <para>
/// <b>A reset window is the life of the process.</b> Nothing refills the global budget, so a reset that
/// never happens leaves the demo read-only, which is the safe direction.
/// </para>
/// </remarks>
public sealed class DemoMutationBudget
{
    /// <summary>Guards every field below.</summary>
    private readonly object _gate = new();

    /// <summary>The visitors' windows by pseudonymous key.</summary>
    private readonly DemoVisitorWindows _visitors;

    /// <summary>The settings.</summary>
    private readonly DemoOptions _options;

    /// <summary>Counted mutations accepted so far.</summary>
    private int _spent;

    /// <summary>Counted mutations accepted in this reset window.</summary>
    public int Spent
    {
        get
        {
            lock (_gate)
            {
                return _spent;
            }
        }
    }

    /// <summary>Visitors currently followed, the overflow bucket included.</summary>
    public int TrackedVisitors
    {
        get
        {
            lock (_gate)
            {
                return _visitors.Count;
            }
        }
    }

    /// <summary>Creates the budget.</summary>
    /// <param name="options">The settings.</param>
    public DemoMutationBudget(IOptions<DemoOptions> options)
    {
        _options = options.Value;
        _visitors = new DemoVisitorWindows(
            _options.MaxTrackedVisitors,
            TimeSpan.FromSeconds(_options.VisitorWindowSeconds));
    }

    /// <summary>Tries to spend one unit for a visitor.</summary>
    /// <param name="visitorKey">The visitor's pseudonymous address.</param>
    /// <param name="now">The current instant, from the clock.</param>
    /// <param name="retryAfter">When refused for the visitor's rate, how long until their window ends.</param>
    /// <returns>Whether the mutation may run, and if not why not.</returns>
    public DemoBudgetDecision TrySpend(string visitorKey, DateTimeOffset now, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        var window = TimeSpan.FromSeconds(_options.VisitorWindowSeconds);

        lock (_gate)
        {
            var visitor = _visitors.Current(visitorKey, now);

            if (visitor.Count >= _options.VisitorMutationsPerWindow)
            {
                retryAfter = visitor.TimeLeft(now, window);
                return DemoBudgetDecision.VisitorRateExceeded;
            }

            if (_spent >= _options.GlobalMutationBudget)
            {
                return DemoBudgetDecision.GlobalExhausted;
            }

            visitor.CountOne();
            _spent++;
            return DemoBudgetDecision.Granted;
        }
    }
}
