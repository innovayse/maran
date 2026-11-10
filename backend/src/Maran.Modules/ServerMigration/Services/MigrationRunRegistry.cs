using System.Collections.Concurrent;
using Maran.Modules.ServerMigration.Models;

namespace Maran.Modules.ServerMigration.Services;

/// <summary>
/// What this process knows about the transfers it is running: which accounts have a worker, how to stop one, and a
/// gate that serialises the scheduler's decisions about one job (issue #197, phase 2).
/// </summary>
/// <remarks>
/// <para>
/// A singleton, because it is the one place the pause of a job, which arrives on one request, reaches the transfer
/// that runs on another. It lives in memory on purpose: a worker is a task of this process, so a process that
/// restarts has no workers, and an account the database still calls running with no worker here is exactly the
/// orphan the pause-and-resume of a job recovers.
/// </para>
/// <para>
/// The gate answers a race the database cannot: two ticks of the scheduler both reading "one slot free" would both
/// start an account and the job would run one more than its limit.
/// </para>
/// </remarks>
public sealed class MigrationRunRegistry
{
    /// <summary>The workers, by the account row they run.</summary>
    private readonly ConcurrentDictionary<Guid, Worker> _workers = new();

    /// <summary>One gate per job.</summary>
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

    /// <summary>Registers a worker for an account and hands out the token the worker watches.</summary>
    /// <param name="accountId">The account row.</param>
    /// <param name="outer">The token of the host's shutdown, which also stops the worker.</param>
    /// <returns>The token that is cancelled when the worker is asked to stop.</returns>
    public CancellationToken Register(Guid accountId, CancellationToken outer)
    {
        var worker = new Worker(CancellationTokenSource.CreateLinkedTokenSource(outer));
        _workers[accountId] = worker;

        return worker.Source.Token;
    }

    /// <summary>Forgets a worker once it ended.</summary>
    /// <param name="accountId">The account row.</param>
    public void Release(Guid accountId)
    {
        if (_workers.TryRemove(accountId, out var worker))
        {
            worker.Source.Dispose();
        }
    }

    /// <summary>Whether this process is running a worker for the account.</summary>
    /// <param name="accountId">The account row.</param>
    /// <returns><c>true</c> while a worker is registered.</returns>
    public bool IsRunning(Guid accountId)
    {
        return _workers.ContainsKey(accountId);
    }

    /// <summary>Asks the worker of an account to stop at its next boundary.</summary>
    /// <param name="accountId">The account row.</param>
    /// <param name="reason">Why it stops.</param>
    public void Stop(Guid accountId, MigrationStopReason reason)
    {
        if (_workers.TryGetValue(accountId, out var worker))
        {
            worker.Reason = reason;
            worker.Source.Cancel();
        }
    }

    /// <summary>Why the worker of an account was asked to stop.</summary>
    /// <param name="accountId">The account row.</param>
    /// <returns>The reason, or <c>null</c> when it was not asked to (the host is shutting down).</returns>
    public MigrationStopReason? ReasonOf(Guid accountId)
    {
        return _workers.TryGetValue(accountId, out var worker) && worker.Source.IsCancellationRequested
            ? worker.Reason
            : null;
    }

    /// <summary>Enters the gate of a job; dispose the result to leave it.</summary>
    /// <param name="jobId">The job.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The held gate.</returns>
    public async Task<IDisposable> EnterAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var gate = _gates.GetOrAdd(jobId, _ => { return new SemaphoreSlim(1, 1); });
        await gate.WaitAsync(cancellationToken);

        return new GateHandle(gate);
    }

    /// <summary>A worker's token source, and why it was told to stop.</summary>
    private sealed class Worker
    {
        /// <summary>Creates the worker's record.</summary>
        /// <param name="source">The token source.</param>
        public Worker(CancellationTokenSource source)
        {
            Source = source;
        }

        /// <summary>The token source the worker watches.</summary>
        public CancellationTokenSource Source { get; }

        /// <summary>Why it was told to stop.</summary>
        public MigrationStopReason Reason { get; set; }
    }

    /// <summary>The held gate of a job.</summary>
    private sealed class GateHandle : IDisposable
    {
        /// <summary>The gate.</summary>
        private readonly SemaphoreSlim _gate;

        /// <summary>Set to 1 by the first release, so a second <see cref="Dispose"/> leaves the gate alone.</summary>
        private int _released;

        /// <summary>Creates the handle.</summary>
        /// <param name="gate">The held gate.</param>
        public GateHandle(SemaphoreSlim gate)
        {
            _gate = gate;
        }

        /// <summary>Leaves the gate, once: a second call is a no-op, as <see cref="IDisposable"/> requires.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _gate.Release();
            }
        }
    }
}
