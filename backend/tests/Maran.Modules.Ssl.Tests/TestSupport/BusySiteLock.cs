using Maran.Sdk.Interfaces;
using Maran.Sdk.Modules;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// An <see cref="ISiteOperationLock"/> that answers <c>SiteBusy</c> at once, whatever the wait, and
/// records what it was asked for.
/// </summary>
/// <remarks>
/// For the tests that need to see how a caller reacts to a busy site without waiting out the real
/// duration the caller asked for (ninety seconds for unattended work).
/// </remarks>
public sealed class BusySiteLock : ISiteOperationLock
{
    /// <summary>The site and wait of every acquisition attempt, in order.</summary>
    public List<(Guid SiteId, TimeSpan Wait)> Attempts { get; } = [];

    /// <inheritdoc />
    public Task<Result<IAsyncDisposable>> AcquireAsync(SiteLockKey key, TimeSpan wait, CancellationToken ct)
    {
        Attempts.Add((key.Id, wait));

        return Task.FromResult(Result<IAsyncDisposable>.Fail(SiteOperationLock.Busy));
    }
}
