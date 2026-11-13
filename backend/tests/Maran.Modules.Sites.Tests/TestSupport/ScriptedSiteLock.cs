using Maran.Sdk.Interfaces;
using Maran.Sdk.Modules;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>
/// An <see cref="ISiteOperationLock"/> a test scripts: it records the wait each request asked for, refuses the sites
/// it is told are busy (spending the whole wait on the clock, as a real refusal does), and can run something right
/// after a site is granted - the moment a change committed during the wait would be visible.
/// </summary>
public sealed class ScriptedSiteLock : ISiteOperationLock
{
    /// <summary>The lock that decides every site that is not scripted busy.</summary>
    private readonly ISiteOperationLock _inner;

    /// <summary>The clock a refusal spends its wait on, or null when time does not matter.</summary>
    private readonly MovableClock? _clock;

    /// <summary>Wraps a lock.</summary>
    /// <param name="inner">The lock that decides every site that is not scripted busy.</param>
    /// <param name="clock">The clock a refusal spends its wait on, or null.</param>
    public ScriptedSiteLock(ISiteOperationLock inner, MovableClock? clock = null)
    {
        _inner = inner;
        _clock = clock;
    }

    /// <summary>The sites that are refused as busy.</summary>
    public HashSet<Guid> Busy { get; } = [];

    /// <summary>Runs after a site is granted, before the caller continues.</summary>
    public Func<Guid, Task>? AfterGrant { get; set; }

    /// <summary>The wait of every request, in order.</summary>
    public List<TimeSpan> Waits { get; } = [];

    /// <inheritdoc />
    public async Task<Result<IAsyncDisposable>> AcquireAsync(SiteLockKey key, TimeSpan wait, CancellationToken ct)
    {
        Waits.Add(wait);
        if (key.Scope == SiteLockScope.Site && Busy.Contains(key.Id))
        {
            _clock?.Advance(wait);

            return Result<IAsyncDisposable>.Fail(SiteOperationLock.Busy);
        }

        var granted = await _inner.AcquireAsync(key, wait, ct);
        if (granted.IsSuccess && AfterGrant is { } after)
        {
            await after(key.Id);
        }

        return granted;
    }
}
