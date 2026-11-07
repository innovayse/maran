using Maran.Modules.Identity.Models;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Lets only one walk along the audit chain run at a time on the panel, and lets callers who arrived
/// while it ran take its answer instead of starting another.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> A verification reads every sealed row, so an administrator who could start many
/// at once could hold the database busy. Rate limiting bounds how often one caller asks; this bounds how
/// many walks exist at the same time, whoever asks and by whatever path (the endpoint and the daily
/// maintenance run both come through the verifier, which comes through here).
/// </para>
/// <para>
/// <b>A waiter reuses a run only if that run STARTED after the waiter arrived.</b> A run already in
/// progress when the waiter arrived may have read rows that changed since, so its answer is not the
/// waiter's. A waiter whose turn comes with no such run runs its own. The run executes in the caller's
/// own scope and database context, never detached from them: a shared background run would outlive the
/// request whose context it read through.
/// </para>
/// <para>
/// A singleton, because the exclusion is only worth anything if every scope shares it.
/// </para>
/// </remarks>
public sealed class AuditChainVerificationGate : IDisposable
{
    /// <summary>Admits one run at a time.</summary>
    private readonly SemaphoreSlim _turn = new(1, 1);

    /// <summary>How many runs have started; a waiter remembers the value it saw on arrival.</summary>
    private long _startedRuns;

    /// <summary>The number of the newest run that finished with an answer; zero before the first.</summary>
    private long _latestFinishedRun;

    /// <summary>The answer of the run numbered <see cref="_latestFinishedRun"/>.</summary>
    private AuditChainVerdict? _latest;

    /// <summary>Runs <paramref name="verify"/>, or returns the answer of a run that started after this call did.</summary>
    /// <param name="verify">The walk to run when no fresher answer exists.</param>
    /// <param name="cancellationToken">Cancels the wait for a turn and, passed on, the walk.</param>
    /// <returns>The answer of a walk that began at or after this call.</returns>
    public async Task<AuditChainVerdict> RunAsync(
        Func<CancellationToken, Task<AuditChainVerdict>> verify,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(verify);

        var seenOnArrival = Interlocked.Read(ref _startedRuns);

        await _turn.WaitAsync(cancellationToken);

        try
        {
            if (_latest is not null && _latestFinishedRun > seenOnArrival)
            {
                return _latest;
            }

            var run = Interlocked.Increment(ref _startedRuns);
            var answer = await verify(cancellationToken);

            _latest = answer;
            _latestFinishedRun = run;

            return answer;
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _turn.Dispose();
    }
}
