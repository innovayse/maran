using System.Diagnostics.CodeAnalysis;
using Maran.Agent.Simulated.Enums;

namespace Maran.Agent.Simulated.State;

/// <summary>
/// One keyed, bounded, thread-safe table of the simulated host's state: accounts, sites,
/// certificates, or whatever else a service remembers.
/// </summary>
/// <typeparam name="TEntry">The immutable record the section holds.</typeparam>
/// <remarks>
/// Every section of a ledger shares the ledger's single lock, so an operation that reads one section
/// and writes another can hold the lock across both through
/// <see cref="SimulatedAgentLedger.Atomically{TResult}"/> and no other thread sees it half done. The
/// lock is reentrant, so a section's own methods compose inside such an operation. An entry is
/// replaced, never edited in place: callers see snapshots that cannot change underneath them.
/// </remarks>
public sealed class SimulatedLedgerSection<TEntry>
    where TEntry : notnull
{
    /// <summary>The ledger's shared lock, taken for every read and write.</summary>
    private readonly object _gate;

    /// <summary>The most entries the section may hold.</summary>
    private readonly int _maxEntries;

    /// <summary>The entries, keyed by their key; touched only under the lock.</summary>
    private readonly Dictionary<string, TEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>The number of entries held right now.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Creates an empty section that locks on <paramref name="gate"/>.</summary>
    /// <param name="gate">The ledger's shared lock.</param>
    /// <param name="maxEntries">The most entries the section may hold.</param>
    public SimulatedLedgerSection(object gate, int maxEntries)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);

        _gate = gate;
        _maxEntries = maxEntries;
    }

    /// <summary>Whether an entry is held under <paramref name="key"/>.</summary>
    /// <param name="key">The entry's key.</param>
    /// <returns>True when the key is present.</returns>
    public bool Contains(string key)
    {
        lock (_gate)
        {
            return _entries.ContainsKey(key);
        }
    }

    /// <summary>Reads the entry held under <paramref name="key"/>.</summary>
    /// <param name="key">The entry's key.</param>
    /// <param name="entry">The entry, when one is held.</param>
    /// <returns>True when the key is present.</returns>
    public bool TryGet(string key, [NotNullWhen(true)] out TEntry? entry)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(key, out entry);
        }
    }

    /// <summary>Stores <paramref name="entry"/> under a key nothing holds yet.</summary>
    /// <param name="key">The entry's key.</param>
    /// <param name="entry">The entry to store.</param>
    /// <returns>How the attempt ended; only <see cref="SimulatedLedgerAdd.Added"/> changed the section.</returns>
    public SimulatedLedgerAdd Add(string key, TEntry entry)
    {
        lock (_gate)
        {
            if (_entries.ContainsKey(key))
            {
                return SimulatedLedgerAdd.AlreadyExists;
            }

            if (_entries.Count >= _maxEntries)
            {
                return SimulatedLedgerAdd.LimitReached;
            }

            _entries.Add(key, entry);

            return SimulatedLedgerAdd.Added;
        }
    }

    /// <summary>Replaces the entry under <paramref name="key"/> with what <paramref name="change"/> builds.</summary>
    /// <param name="key">The entry's key.</param>
    /// <param name="change">Builds the new entry from the current one.</param>
    /// <returns>True when an entry was held and replaced; false when the key was absent.</returns>
    public bool Replace(string key, Func<TEntry, TEntry> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var current))
            {
                return false;
            }

            _entries[key] = change(current);

            return true;
        }
    }

    /// <summary>Removes the entry held under <paramref name="key"/>.</summary>
    /// <param name="key">The entry's key.</param>
    /// <returns>True when an entry was removed; false when the key was absent.</returns>
    public bool Remove(string key)
    {
        lock (_gate)
        {
            return _entries.Remove(key);
        }
    }

    /// <summary>Copies the entries, ordered by key (ordinal), so a listing is the same on every call.</summary>
    /// <returns>A list that later changes to the section do not affect.</returns>
    public IReadOnlyList<TEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries
                .OrderBy(pair => { return pair.Key; }, StringComparer.Ordinal)
                .Select(pair => { return pair.Value; })
                .ToList();
        }
    }
}
