using Maran.Host.Demo.Guard;
using Maran.Host.Demo.Guard.Enums;
using Maran.Host.Demo.Options;
using Maran.Host.Demo.Tests.Fixtures;
using Microsoft.Extensions.Options;

namespace Maran.Host.Demo.Tests.Guard;

/// <summary>Sign-ins, refreshes and sign-outs are bounded per visitor and for the whole demo, and renew.</summary>
public sealed class DemoSignInBudgetTests
{
    /// <summary>The instant the tests start from.</summary>
    private static readonly DateTimeOffset Start = new FixedClock().UtcNow;

    /// <summary>Builds a budget over small numbers.</summary>
    /// <param name="perVisitor">Sign-ins per visitor window.</param>
    /// <param name="global">Sign-ins per window for the whole demo.</param>
    /// <returns>The budget.</returns>
    private static DemoSignInBudget Budget(int perVisitor, int global)
    {
        return new DemoSignInBudget(
            new OptionsWrapper<DemoOptions>(new DemoOptions
            {
                VisitorSignInsPerWindow = perVisitor,
                GlobalSignInsPerWindow = global,
                VisitorWindowSeconds = 60,
            }),
            new FixedClock());
    }

    /// <summary>A visitor over their allowance is refused with the time left.</summary>
    [Fact]
    public void A_visitor_over_their_allowance_is_refused_with_the_time_left()
    {
        var budget = Budget(perVisitor: 2, global: 100);
        budget.TryTake("a", Start, out _);
        budget.TryTake("a", Start, out _);

        var third = budget.TryTake("a", Start.AddSeconds(20), out var retryAfter);

        Assert.Equal(DemoBudgetDecision.VisitorRateExceeded, third);
        Assert.Equal(TimeSpan.FromSeconds(40), retryAfter);
    }

    /// <summary>The control: another visitor is not limited by the first one's allowance.</summary>
    [Fact]
    public void Another_visitor_is_not_limited_by_the_first()
    {
        var budget = Budget(perVisitor: 1, global: 100);
        budget.TryTake("a", Start, out _);

        Assert.Equal(DemoBudgetDecision.Granted, budget.TryTake("b", Start, out _));
    }

    /// <summary>The whole demo's allowance caps the rate however many visitors there are, and renews.</summary>
    [Fact]
    public void The_global_allowance_caps_every_visitor_and_renews_with_the_window()
    {
        var budget = Budget(perVisitor: 5, global: 2);
        budget.TryTake("a", Start, out _);
        budget.TryTake("b", Start, out _);

        Assert.Equal(DemoBudgetDecision.GlobalExhausted, budget.TryTake("c", Start, out _));
        Assert.Equal(DemoBudgetDecision.Granted, budget.TryTake("c", Start.AddSeconds(61), out _));
    }

    /// <summary>A visitor refused for the global allowance has spent nothing of their own.</summary>
    [Fact]
    public void A_global_refusal_spends_nothing_of_the_visitors_allowance()
    {
        var budget = Budget(perVisitor: 1, global: 1);
        budget.TryTake("a", Start, out _);

        // b's own window begins at 30 s and the demo's renews at 60 s, so a wrongly spent unit of b's would still
        // be standing at 61 s and b would be refused for their own rate.
        Assert.Equal(DemoBudgetDecision.GlobalExhausted, budget.TryTake("b", Start.AddSeconds(30), out _));
        Assert.Equal(DemoBudgetDecision.Granted, budget.TryTake("b", Start.AddSeconds(61), out _));
    }
}
