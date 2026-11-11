using System.Collections.Concurrent;

namespace Maran.Modules.Mail.Services;

/// <summary>
/// Serialises everything that changes one system user's local submission binding (issue #518): a push, a
/// removal and the ledger row that records them take turns per user.
/// </summary>
/// <remarks>
/// <para>
/// Without it a domain change that read the account before a suspension and pushed after the removal would
/// bind the suspended account again. Inside the gate the publisher reads the ledger, so the question "was this
/// account withdrawn?" is answered after every earlier removal has finished and before any later one starts.
/// </para>
/// <para>
/// A singleton, because the panel is one process per server and the gate must outlive the request scopes that
/// take it. One semaphore per user name is kept for the life of the process; the set is bounded by the accounts
/// on the host.
/// </para>
/// </remarks>
public sealed class MailSubmissionGate
{
    /// <summary>One semaphore per system user name.</summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    /// <summary>Waits for the user's turn.</summary>
    /// <param name="username">System username of the account.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The turn; disposing it hands the gate to the next caller.</returns>
    public async Task<IDisposable> EnterAsync(string username, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(username, _ =>
        {
            return new SemaphoreSlim(1, 1);
        });
        await gate.WaitAsync(cancellationToken);

        return new Turn(gate);
    }

    /// <summary>A held turn that releases the semaphore once.</summary>
    private sealed class Turn : IDisposable
    {
        /// <summary>The semaphore this turn holds.</summary>
        private readonly SemaphoreSlim _gate;

        /// <summary>Whether the turn was already given back.</summary>
        private int _released;

        /// <summary>Creates the turn.</summary>
        /// <param name="gate">The semaphore already acquired.</param>
        public Turn(SemaphoreSlim gate)
        {
            _gate = gate;
        }

        /// <summary>Gives the turn back; a second call does nothing.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _gate.Release();
            }
        }
    }
}
