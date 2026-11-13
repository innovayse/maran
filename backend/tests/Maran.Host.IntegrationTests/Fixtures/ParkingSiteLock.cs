using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// An <see cref="ISiteOperationLock"/> that tells a test when a caller is really waiting for a site, so a test
/// proves "the second operation is blocked" by an event instead of by waiting a while and seeing nothing happen.
/// </summary>
/// <remarks>
/// A request is parked when the inner lock could not answer it at once. With a lock that did not hold, no
/// request ever parks and <see cref="Parked"/> never completes, so the test fails (after its hang timeout)
/// rather than passing vacuously on a slow machine.
/// </remarks>
public sealed class ParkingSiteLock : ISiteOperationLock
{
    /// <summary>The lock that really decides.</summary>
    private readonly ISiteOperationLock _inner;

    /// <summary>Completed when the first request parks.</summary>
    private readonly TaskCompletionSource _parked = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Wraps a lock.</summary>
    /// <param name="inner">The lock that really decides.</param>
    public ParkingSiteLock(ISiteOperationLock inner)
    {
        _inner = inner;
    }

    /// <summary>Completes once a request is waiting for a site that is in use.</summary>
    public Task Parked
    {
        get
        {
            return _parked.Task;
        }
    }

    /// <inheritdoc />
    public async Task<Result<IAsyncDisposable>> AcquireAsync(SiteLockKey key, TimeSpan wait, CancellationToken ct)
    {
        var pending = _inner.AcquireAsync(key, wait, ct);
        if (!pending.IsCompleted)
        {
            _parked.TrySetResult();
        }

        return await pending;
    }
}
