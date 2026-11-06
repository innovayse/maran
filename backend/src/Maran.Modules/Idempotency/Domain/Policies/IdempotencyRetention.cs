namespace Maran.Modules.Idempotency.Domain.Policies;

/// <summary>How long a stored idempotency record lives, in each of its two states.</summary>
public static class IdempotencyRetention
{
    /// <summary>
    /// How long a completed record can be replayed, counted from the moment the key was first used. A
    /// client retries within minutes or hours, not days, and a stored response is a copy of customer
    /// data at rest in clear, so the window is as short as a retry plausibly needs.
    /// </summary>
    public static readonly TimeSpan CompletedWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// How long a reservation holds its key while the request runs. A process that dies with a
    /// reservation held would otherwise answer "in progress" to every retry for the whole completed
    /// window; after the lease the key can be taken again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why five minutes.</b> It is the longest a caller should be told "in progress" about a request
    /// whose process has died, and a retrying client gives up well before an hour. Longer would make a
    /// crash cost every retry that much; shorter would let an ordinary slow request lose its key while
    /// it still runs.
    /// </para>
    /// <para>
    /// <b>Stated limit.</b> An operation that legitimately runs longer than the lease (deleting an
    /// account takes a final backup first) can outlive it. A retry that arrives after the lease has
    /// lapsed while the first request is still running takes the key and runs the work a second time,
    /// because nothing here can tell a dead process from a slow one. What the lease no longer allows is
    /// the two requests tangling their records: each reservation carries its own token, so the slow
    /// request's late completion or release is ignored instead of completing or freeing the key of the
    /// request that replaced it. The duplicated work is the residue; the corrupted record is not.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan ReservationLease = TimeSpan.FromMinutes(5);
}
