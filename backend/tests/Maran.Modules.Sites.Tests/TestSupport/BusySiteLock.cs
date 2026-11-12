using Maran.Sdk.Interfaces;
using Maran.Sdk.Modules;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>An <see cref="ISiteOperationLock"/> whose every site is in use for good, so a refusal needs no waiting.</summary>
public sealed class BusySiteLock : ISiteOperationLock
{
    /// <summary>How many times the lock was asked for.</summary>
    public int Attempts { get; private set; }

    /// <inheritdoc/>
    public Task<Result<IAsyncDisposable>> AcquireAsync(SiteLockKey key, TimeSpan wait, CancellationToken ct)
    {
        Attempts++;
        return Task.FromResult(Result<IAsyncDisposable>.Fail(SiteOperationLock.Busy));
    }
}
