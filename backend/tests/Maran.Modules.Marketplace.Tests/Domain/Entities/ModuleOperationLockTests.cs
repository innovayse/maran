using Maran.Modules.Marketplace.Domain.Entities;

namespace Maran.Modules.Marketplace.Tests.Domain.Entities;

/// <summary>A claim on a module goes stale after thirty minutes and not a moment before.</summary>
public sealed class ModuleOperationLockTests
{
    /// <summary>The instant the claim is made.</summary>
    private static readonly DateTimeOffset At = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The stale threshold is thirty minutes.</summary>
    [Fact]
    public void The_stale_threshold_is_thirty_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), ModuleOperationLock.StaleAfter);
    }

    /// <summary>A claim exactly thirty minutes old is still held; one a second older is stale.</summary>
    [Fact]
    public void A_claim_is_stale_only_after_the_threshold_has_passed()
    {
        var claim = new ModuleOperationLock(Guid.NewGuid(), "mail", At);

        Assert.False(claim.IsStale(At));
        Assert.False(claim.IsStale(At + ModuleOperationLock.StaleAfter));
        Assert.True(claim.IsStale(At + ModuleOperationLock.StaleAfter + TimeSpan.FromSeconds(1)));
    }
}
