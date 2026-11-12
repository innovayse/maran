using Maran.Modules.Licensing.Domain.Enums;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// The wait between two renewal passes, which a pass somebody asked for ends early: the one place the schedule's
/// sleeping and the "a pass just ran, wait as its outcome says" signal meet.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a pass that ran out of turn ends the wait.</b> The wait the loop is in was chosen from the outcome of the
/// pass before it, often the idle hour of a panel that had no licence. When an administrator's install then runs a
/// pass, that wait describes a world that no longer exists: left alone, a request that failed would be retried in
/// the idle hour instead of the failure wait, and one that stored a lease would be followed by a pointless second
/// request. Ending the wait lets the loop choose the next one from the outcome that actually happened.
/// </para>
/// <para>
/// <b>Only a wait that is under way can be ended.</b> A pass that completes while the loop is running its own pass
/// is not remembered: the loop's own pass, which finishes after it, decides the next wait, and a stale outcome
/// from the pass that merely ran beside it would replace the one the loop earned. Each wait starts from a fresh
/// signal, and one pass ends one wait. The loop is the only waiter.
/// </para>
/// </remarks>
public sealed class LeaseWaitSignal
{
    /// <summary>The signal of the wait under way, completed by a pass that ran out of turn; null when nobody is waiting.</summary>
    private TaskCompletionSource<LeaseRenewalOutcome>? _passed;

    /// <summary>Records that a pass ran out of turn, which ends the current wait; nothing is recorded when no wait is under way.</summary>
    /// <param name="outcome">What that pass amounted to.</param>
    public void Notify(LeaseRenewalOutcome outcome)
    {
        Volatile.Read(ref _passed)?.TrySetResult(outcome);
    }

    /// <summary>Waits for the delay to pass or for a pass that ran out of turn, whichever comes first.</summary>
    /// <param name="delay">How long the schedule waits.</param>
    /// <param name="cancellationToken">Cancelled when the host is shutting down.</param>
    /// <returns>The outcome of the pass that ended the wait; null when the delay passed.</returns>
    /// <exception cref="OperationCanceledException">The host is shutting down.</exception>
    public async Task<LeaseRenewalOutcome?> WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var signal = NewSignal();
        Volatile.Write(ref _passed, signal);
        try
        {
            using var timerStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timer = Task.Delay(delay, timerStop.Token);

            var first = await Task.WhenAny(timer, signal.Task);
            await timerStop.CancelAsync();

            if (first == signal.Task)
            {
                return await signal.Task;
            }

            // The delay passed, or the host is stopping: a stop surfaces here as the cancellation the loop ends on.
            await timer;

            return null;
        }
        finally
        {
            // Nobody waits any more: a pass that completes now is not for the next wait.
            Volatile.Write(ref _passed, null);
        }
    }

    /// <summary>A signal no pass has completed yet.</summary>
    /// <returns>The signal.</returns>
    private static TaskCompletionSource<LeaseRenewalOutcome> NewSignal()
    {
        return new TaskCompletionSource<LeaseRenewalOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
