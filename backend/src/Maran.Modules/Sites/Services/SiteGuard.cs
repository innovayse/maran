using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Runs one operation on a site while holding that site's <see cref="ISiteOperationLock"/>, so the
/// handlers that mutate a site's host state take the lock the same way and cannot forget to release it.
/// </summary>
/// <remarks>
/// A handler calls this after its cheap validation and before it loads the site row: the row is read
/// INSIDE the guard, so what the body sees is what no other operation can change until it returns. The
/// lock is held across the body, including the save, and released by disposal on every path - success,
/// failure and exception alike. When the site stays busy for the whole wait the body does not run and the
/// caller gets the <c>SiteBusy</c> conflict.
/// </remarks>
public static class SiteGuard
{
    /// <summary>Runs a body that yields a value while the site is held.</summary>
    /// <typeparam name="T">The value the body yields.</typeparam>
    /// <param name="siteLock">The shared per-site lock.</param>
    /// <param name="key">The site to hold, or the account's pools.</param>
    /// <param name="wait">How long to wait for the site to be free.</param>
    /// <param name="body">The operation, run only once the site is held.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>The body's result, or the <c>SiteBusy</c> failure when the site was not free in time.</returns>
    public static async Task<Result<T>> RunAsync<T>(
        ISiteOperationLock siteLock,
        SiteLockKey key,
        TimeSpan wait,
        Func<Task<Result<T>>> body,
        CancellationToken ct)
    {
        var held = await siteLock.AcquireAsync(key, wait, ct);
        if (!held.IsSuccess)
        {
            return Result<T>.Fail(held.Error!);
        }

        await using (held.Value)
        {
            return await body();
        }
    }

    /// <summary>Runs a body that yields no value while the site is held.</summary>
    /// <param name="siteLock">The shared per-site lock.</param>
    /// <param name="key">The site to hold, or the account's pools.</param>
    /// <param name="wait">How long to wait for the site to be free.</param>
    /// <param name="body">The operation, run only once the site is held.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>The body's result, or the <c>SiteBusy</c> failure when the site was not free in time.</returns>
    public static async Task<Result> RunAsync(
        ISiteOperationLock siteLock,
        SiteLockKey key,
        TimeSpan wait,
        Func<Task<Result>> body,
        CancellationToken ct)
    {
        var held = await siteLock.AcquireAsync(key, wait, ct);
        if (!held.IsSuccess)
        {
            return Result.Fail(held.Error!);
        }

        await using (held.Value)
        {
            return await body();
        }
    }
}
