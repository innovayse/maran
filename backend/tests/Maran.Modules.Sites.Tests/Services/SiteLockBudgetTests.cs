using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;

namespace Maran.Modules.Sites.Tests.Services;

/// <summary>The waiting time an account cascade may spend on all its sites together.</summary>
public sealed class SiteLockBudgetTests
{
    /// <summary>A fresh budget gives a site the full per-site wait.</summary>
    [Fact]
    public void A_fresh_budget_gives_a_site_the_full_wait()
    {
        var budget = new SiteLockBudget(new MovableClock());

        Assert.Equal(SiteLockWait.Cascade, budget.NextWait());
    }

    /// <summary>What is left of the total caps the wait of the site that would overrun it.</summary>
    [Fact]
    public void What_is_left_caps_the_wait()
    {
        var clock = new MovableClock();
        var budget = new SiteLockBudget(clock);

        clock.Advance(SiteLockWait.CascadeTotal - TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(5), budget.NextWait());
    }

    /// <summary>A spent budget asks for a site without waiting, and stays spent.</summary>
    [Fact]
    public void A_spent_budget_does_not_wait()
    {
        var clock = new MovableClock();
        var budget = new SiteLockBudget(clock);

        clock.Advance(SiteLockWait.CascadeTotal);
        Assert.Equal(TimeSpan.Zero, budget.NextWait());

        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.Zero, budget.NextWait());
    }
}
