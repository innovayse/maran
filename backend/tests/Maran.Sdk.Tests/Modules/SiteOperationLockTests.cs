using Maran.Sdk.Interfaces;
using Maran.Sdk.Modules;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Sdk.Tests.Modules;

/// <summary>
/// The per-site lock: one holder per site at a time, a bounded wait that answers with a typed conflict
/// instead of throwing, independent sites, cancellation that propagates, idempotent release, and a table
/// that does not grow with the sites it has seen.
/// </summary>
public sealed class SiteOperationLockTests
{
    /// <summary>A site.</summary>
    private static readonly Guid SiteA = Guid.NewGuid();

    /// <summary>Another site.</summary>
    private static readonly Guid SiteB = Guid.NewGuid();

    /// <summary>Long enough that a test never waits it out unless it means to.</summary>
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    /// <summary>A second request for a held site waits for the first and proceeds the moment it is released.</summary>
    [Fact]
    public async Task A_second_request_for_a_held_site_is_admitted_only_after_the_first_releases()
    {
        var siteLock = new SiteOperationLock();
        var first = await siteLock.AcquireAsync(SiteA, Generous, CancellationToken.None);

        // No pause needed: a lock that did not hold would answer on the spot (the free path completes
        // synchronously), so a second request that is still pending the moment the call returns is waiting.
        var second = siteLock.AcquireAsync(SiteA, Generous, CancellationToken.None);
        Assert.False(second.IsCompleted);

        await first.Value.DisposeAsync();
        var admitted = await second.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(admitted.IsSuccess);
    }

    /// <summary>Only one of many concurrent holders is ever inside at once.</summary>
    [Fact]
    public async Task Concurrent_holders_of_one_site_never_overlap()
    {
        var siteLock = new SiteOperationLock();
        var inside = 0;
        var overlapped = false;

        await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            var held = await siteLock.AcquireAsync(SiteA, Generous, CancellationToken.None);
            await using (held.Value)
            {
                if (Interlocked.Increment(ref inside) > 1)
                {
                    overlapped = true;
                }

                await Task.Delay(2);
                Interlocked.Decrement(ref inside);
            }
        }));

        Assert.False(overlapped);
    }

    /// <summary>A held site answers a zero wait with the SiteBusy conflict and does not throw.</summary>
    [Fact]
    public async Task A_held_site_answers_a_zero_wait_with_site_busy_as_a_conflict()
    {
        var siteLock = new SiteOperationLock();
        await using var held = (await siteLock.AcquireAsync(SiteA, Generous, CancellationToken.None)).Value;

        var refused = await siteLock.AcquireAsync(SiteA, TimeSpan.Zero, CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal("SiteBusy", refused.Error!.Code);
        Assert.Equal(ErrorType.Conflict, refused.Error.Type);
    }

    /// <summary>A wait that runs out answers SiteBusy rather than throwing a timeout.</summary>
    [Fact]
    public async Task A_wait_that_runs_out_answers_site_busy()
    {
        var siteLock = new SiteOperationLock();
        await using var held = (await siteLock.AcquireAsync(SiteA, Generous, CancellationToken.None)).Value;

        var refused = await siteLock.AcquireAsync(SiteA, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        Assert.Equal("SiteBusy", refused.Error!.Code);
    }

    /// <summary>Control: a free site is admitted at once even with a zero wait.</summary>
    [Fact]
    public async Task A_free_site_is_admitted_with_a_zero_wait()
    {
        var siteLock = new SiteOperationLock();

        var admitted = await siteLock.AcquireAsync(SiteA, TimeSpan.Zero, CancellationToken.None);

        Assert.True(admitted.IsSuccess);
        await admitted.Value.DisposeAsync();
    }

    /// <summary>Control: holding one site never delays another.</summary>
    [Fact]
    public async Task Holding_one_site_does_not_block_another()
    {
        var siteLock = new SiteOperationLock();
        await using var held = (await siteLock.AcquireAsync(SiteA, Generous, CancellationToken.None)).Value;

        var other = await siteLock.AcquireAsync(SiteB, TimeSpan.Zero, CancellationToken.None);

        Assert.True(other.IsSuccess);
        await other.Value.DisposeAsync();
    }

    /// <summary>
    /// An account's pools and a site are different things even when their ids are equal: holding one never
    /// blocks the other, and the same kind of key still excludes itself (the accepting control).
    /// </summary>
    [Fact]
    public async Task An_accounts_pools_and_a_site_with_the_same_id_are_separate_locks()
    {
        var siteLock = new SiteOperationLock();
        await using var site = (await siteLock.AcquireAsync(SiteLockKey.ForSite(SiteA), Generous, CancellationToken.None)).Value;

        var pools = await siteLock.AcquireAsync(SiteLockKey.ForPhpPools(SiteA), TimeSpan.Zero, CancellationToken.None);
        var again = await siteLock.AcquireAsync(SiteLockKey.ForSite(SiteA), TimeSpan.Zero, CancellationToken.None);

        Assert.True(pools.IsSuccess);
        Assert.False(again.IsSuccess);
        await pools.Value.DisposeAsync();
    }

    /// <summary>Cancelling a wait throws, and the site is still usable afterwards.</summary>
    [Fact]
    public async Task Cancelling_a_wait_throws_and_leaves_the_site_usable()
    {
        var siteLock = new SiteOperationLock();
        var held = (await siteLock.AcquireAsync(SiteA, Generous, CancellationToken.None)).Value;
        using var source = new CancellationTokenSource();

        var waiting = siteLock.AcquireAsync(SiteA, Generous, source.Token);
        await source.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
        {
            return waiting;
        });

        await held.DisposeAsync();
        var again = await siteLock.AcquireAsync(SiteA, TimeSpan.Zero, CancellationToken.None);
        Assert.True(again.IsSuccess);
        await again.Value.DisposeAsync();
        Assert.Equal(0, siteLock.TrackedSiteCount);
    }

    /// <summary>Releasing twice releases once: a second dispose cannot admit two holders.</summary>
    [Fact]
    public async Task Releasing_twice_does_not_admit_a_second_holder()
    {
        var siteLock = new SiteOperationLock();
        var first = (await siteLock.AcquireAsync(SiteA, Generous, CancellationToken.None)).Value;
        await first.DisposeAsync();
        await using var second = (await siteLock.AcquireAsync(SiteA, Generous, CancellationToken.None)).Value;

        await first.DisposeAsync();
        var third = await siteLock.AcquireAsync(SiteA, TimeSpan.Zero, CancellationToken.None);

        Assert.False(third.IsSuccess);
    }

    /// <summary>The table holds only sites in use: released and refused sites leave no entry behind.</summary>
    [Fact]
    public async Task Entries_are_removed_once_a_site_is_no_longer_in_use()
    {
        var siteLock = new SiteOperationLock();

        for (var index = 0; index < 50; index++)
        {
            var held = await siteLock.AcquireAsync(Guid.NewGuid(), Generous, CancellationToken.None);
            await held.Value.DisposeAsync();
        }

        var busy = (await siteLock.AcquireAsync(SiteA, Generous, CancellationToken.None)).Value;
        await siteLock.AcquireAsync(SiteA, TimeSpan.Zero, CancellationToken.None);
        Assert.Equal(1, siteLock.TrackedSiteCount);

        await busy.DisposeAsync();
        Assert.Equal(0, siteLock.TrackedSiteCount);
    }

    /// <summary>A negative wait is a programming error and throws.</summary>
    [Fact]
    public async Task A_negative_wait_throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
        {
            return new SiteOperationLock().AcquireAsync(SiteA, TimeSpan.FromSeconds(-1), CancellationToken.None);
        });
    }

    /// <summary>The Sdk registers one shared instance, so Sites and Ssl exclude each other.</summary>
    [Fact]
    public void The_sdk_registers_one_shared_instance_of_the_lock()
    {
        var provider = new ServiceCollection().AddSdk().BuildServiceProvider();

        var first = provider.GetRequiredService<ISiteOperationLock>();
        var second = provider.CreateScope().ServiceProvider.GetRequiredService<ISiteOperationLock>();

        Assert.IsType<SiteOperationLock>(first);
        Assert.Same(first, second);
    }
}
