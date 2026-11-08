namespace Maran.Agent.Simulated.State;

/// <summary>
/// A single thread-safe slot of the simulated host's state, for a fact that exists once on a host
/// (the panel's own certificate, the host's facts) and so has no key to be filed under.
/// </summary>
/// <typeparam name="TValue">The immutable record the slot holds.</typeparam>
public sealed class SimulatedLedgerCell<TValue>
    where TValue : class
{
    /// <summary>The ledger's shared lock, taken for every read and write.</summary>
    private readonly object _gate;

    /// <summary>What the slot holds, or null when it is empty; touched only under the lock.</summary>
    private TValue? _value;

    /// <summary>What the slot holds, or null when it is empty.</summary>
    public TValue? Value
    {
        get
        {
            lock (_gate)
            {
                return _value;
            }
        }
    }

    /// <summary>Creates a slot that locks on <paramref name="gate"/>.</summary>
    /// <param name="gate">The ledger's shared lock.</param>
    /// <param name="initial">What the slot holds to begin with, or null for an empty slot.</param>
    public SimulatedLedgerCell(object gate, TValue? initial)
    {
        ArgumentNullException.ThrowIfNull(gate);

        _gate = gate;
        _value = initial;
    }

    /// <summary>What the slot holds, or <paramref name="fallback"/> when it is empty.</summary>
    /// <param name="fallback">The value to answer with when nothing has been put in the slot.</param>
    /// <returns>The held value, else the fallback.</returns>
    public TValue ValueOr(TValue fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);

        lock (_gate)
        {
            return _value ?? fallback;
        }
    }

    /// <summary>Replaces what the slot holds.</summary>
    /// <param name="value">The new value.</param>
    public void Replace(TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (_gate)
        {
            _value = value;
        }
    }
}
