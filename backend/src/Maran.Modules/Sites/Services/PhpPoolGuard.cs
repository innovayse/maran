using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Runs an operation while holding the lock of an ACCOUNT's php-fpm pools, the resource a per-site lock
/// cannot cover.
/// </summary>
/// <remarks>
/// <para>
/// A pool belongs to an account and a version and is shared by every site of the account on that version.
/// "Is this the last site on version V" is therefore a question about the account, and the answer is acted
/// on at the agent (a pool is created or removed). Two operations on DIFFERENT sites can disagree: a delete
/// of the last site on 8.2 reads no other site there while a version change of another site is moving to
/// 8.2, so the delete retires the pool the other site has just been given. Both take this lock around the
/// question, the agent call and the save, so the second reads rows that already include the first's change.
/// </para>
/// <para>
/// It is the same <see cref="ISiteOperationLock"/> taken on the account's own kind of key
/// (<see cref="SiteLockKey.ForPhpPools"/>), so it needs no second facility and cannot be mistaken for a site. The order is fixed: a site's own lock first, this one second, never the reverse, so
/// two operations cannot wait for each other.
/// </para>
/// </remarks>
public static class PhpPoolGuard
{
    /// <summary>Runs a body that yields a value while the account's pools are held.</summary>
    /// <typeparam name="T">The value the body yields.</typeparam>
    /// <param name="siteLock">The shared lock.</param>
    /// <param name="accountId">The account whose pools are decided.</param>
    /// <param name="wait">How long to wait for the pools to be free.</param>
    /// <param name="body">The operation, run only once the pools are held.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>The body's result, or the <c>SiteBusy</c> failure when the pools were not free in time.</returns>
    public static Task<Result<T>> RunAsync<T>(
        ISiteOperationLock siteLock,
        Guid accountId,
        TimeSpan wait,
        Func<Task<Result<T>>> body,
        CancellationToken ct)
    {
        return SiteGuard.RunAsync(siteLock, SiteLockKey.ForPhpPools(accountId), wait, body, ct);
    }

    /// <summary>Runs a body that yields no value while the account's pools are held.</summary>
    /// <param name="siteLock">The shared lock.</param>
    /// <param name="accountId">The account whose pools are decided.</param>
    /// <param name="wait">How long to wait for the pools to be free.</param>
    /// <param name="body">The operation, run only once the pools are held.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>The body's result, or the <c>SiteBusy</c> failure when the pools were not free in time.</returns>
    public static Task<Result> RunAsync(
        ISiteOperationLock siteLock,
        Guid accountId,
        TimeSpan wait,
        Func<Task<Result>> body,
        CancellationToken ct)
    {
        return SiteGuard.RunAsync(siteLock, SiteLockKey.ForPhpPools(accountId), wait, body, ct);
    }
}
