using Maran.Host.Configuration;
using Microsoft.Extensions.Options;

namespace Maran.Host.Dashboard;

/// <summary>
/// Counts the live dashboard streams the whole process holds open and refuses one past
/// <see cref="DashboardStreamOptions.PerProcessStreamLimit"/>.
/// </summary>
/// <remarks>
/// The process-wide half of the stream's concurrency bound; the per-user half is
/// <c>DashboardStreamRateLimitPolicy</c>. It never waits: a stream over the limit is refused at once,
/// because waiting would hold open the very thing being rationed. A singleton, since the count is the
/// state; the admission is one atomic compare-and-increment, so two streams racing for the last slot
/// cannot both get it.
/// </remarks>
public sealed class DashboardStreamGate
{
    /// <summary>The most streams the process may hold.</summary>
    private readonly int _limit;

    /// <summary>How many are open now.</summary>
    private int _open;

    /// <summary>How many streams are open now.</summary>
    public int Open
    {
        get
        {
            return Volatile.Read(ref _open);
        }
    }

    /// <summary>Creates the gate.</summary>
    /// <param name="options">The validated stream settings.</param>
    public DashboardStreamGate(IOptions<DashboardStreamOptions> options)
    {
        _limit = options.Value.PerProcessStreamLimit;
    }

    /// <summary>Takes a slot if one is free.</summary>
    /// <returns>A lease to dispose when the stream ends, or <c>null</c> when the process is at its limit.</returns>
    public IDisposable? TryEnter()
    {
        while (true)
        {
            var open = Volatile.Read(ref _open);
            if (open >= _limit)
            {
                return null;
            }

            if (Interlocked.CompareExchange(ref _open, open + 1, open) == open)
            {
                return new Lease(this);
            }
        }
    }

    /// <summary>One taken slot, returned exactly once however many times it is disposed.</summary>
    private sealed class Lease : IDisposable
    {
        /// <summary>The gate to return the slot to.</summary>
        private readonly DashboardStreamGate _gate;

        /// <summary>Set to 1 when the slot has been returned.</summary>
        private int _released;

        /// <summary>Creates a lease on a slot already counted.</summary>
        /// <param name="gate">The gate that counted it.</param>
        public Lease(DashboardStreamGate gate)
        {
            _gate = gate;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                Interlocked.Decrement(ref _gate._open);
            }
        }
    }
}
