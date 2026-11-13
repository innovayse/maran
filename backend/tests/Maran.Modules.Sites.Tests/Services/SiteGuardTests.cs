using Maran.Modules.Sites.Services;
using Maran.Sdk.Modules;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Sites.Tests.Services;

/// <summary>
/// The guard runs a site operation only while it holds the site, releases the site on every path, and
/// answers a busy site with the typed conflict without running the operation at all.
/// </summary>
public sealed class SiteGuardTests
{
    /// <summary>The site under test.</summary>
    private static readonly Guid Site = Guid.NewGuid();

    /// <summary>A busy site is refused with SiteBusy and the body never runs.</summary>
    [Fact]
    public async Task A_busy_site_is_refused_and_the_body_does_not_run()
    {
        var siteLock = new SiteOperationLock();
        await using var held = (await siteLock.AcquireAsync(Site, TimeSpan.FromSeconds(30), CancellationToken.None)).Value;
        var ran = false;

        var result = await SiteGuard.RunAsync(siteLock, Site, TimeSpan.Zero, () =>
        {
            ran = true;

            return Task.FromResult(Result.Ok());
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteBusy", result.Error!.Code);
        Assert.False(ran);
    }

    /// <summary>Control: a free site runs the body, returns its value and is free again afterwards.</summary>
    [Fact]
    public async Task A_free_site_runs_the_body_and_is_released_afterwards()
    {
        var siteLock = new SiteOperationLock();

        var result = await SiteGuard.RunAsync(siteLock, Site, TimeSpan.Zero, () =>
        {
            return Task.FromResult(Result<int>.Ok(7));
        }, CancellationToken.None);

        Assert.Equal(7, result.Value);
        Assert.Equal(0, siteLock.TrackedSiteCount);
    }

    /// <summary>The site is held while the body runs and released when the body throws.</summary>
    [Fact]
    public async Task The_site_is_held_during_the_body_and_released_when_it_throws()
    {
        var siteLock = new SiteOperationLock();
        var heldDuringBody = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            return SiteGuard.RunAsync<int>(siteLock, Site, TimeSpan.Zero, async () =>
                    {
                        heldDuringBody = !(await siteLock.AcquireAsync(Site, TimeSpan.Zero, CancellationToken.None)).IsSuccess;
                        throw new InvalidOperationException("boom");
                    }, CancellationToken.None);
        });

        Assert.True(heldDuringBody);
        Assert.Equal(0, siteLock.TrackedSiteCount);
    }
}
