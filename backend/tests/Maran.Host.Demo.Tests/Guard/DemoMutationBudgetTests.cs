using Maran.Host.Demo.Guard;
using Maran.Host.Demo.Guard.Enums;
using Maran.Host.Demo.Options;
using Microsoft.Extensions.Options;

namespace Maran.Host.Demo.Tests.Guard;

/// <summary>The budgets are enforced, bounded, and spend in the right order.</summary>
public sealed class DemoMutationBudgetTests
{
    /// <summary>The instant the tests start from.</summary>
    private static readonly DateTimeOffset Start = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Builds a budget over small numbers.</summary>
    /// <param name="global">The global budget.</param>
    /// <param name="perVisitor">Mutations per visitor window.</param>
    /// <param name="tracked">Visitors tracked.</param>
    /// <returns>The budget.</returns>
    private static DemoMutationBudget Budget(int global, int perVisitor, int tracked = 100)
    {
        return new DemoMutationBudget(new OptionsWrapper<DemoOptions>(new DemoOptions
        {
            GlobalMutationBudget = global,
            VisitorMutationsPerWindow = perVisitor,
            VisitorWindowSeconds = 60,
            MaxTrackedVisitors = tracked,
        }));
    }

    /// <summary>A visitor over their own rate is refused with the time left, and spends nothing.</summary>
    [Fact]
    public void A_visitor_over_their_rate_is_refused_with_the_time_left()
    {
        var budget = Budget(global: 100, perVisitor: 2);

        Assert.Equal(DemoBudgetDecision.Granted, budget.TrySpend("a", Start, out _));
        Assert.Equal(DemoBudgetDecision.Granted, budget.TrySpend("a", Start, out _));
        var third = budget.TrySpend("a", Start.AddSeconds(20), out var retryAfter);

        Assert.Equal(DemoBudgetDecision.VisitorRateExceeded, third);
        Assert.Equal(TimeSpan.FromSeconds(40), retryAfter);
        Assert.Equal(2, budget.Spent);
    }

    /// <summary>The visitor's window rolls over and they may mutate again.</summary>
    [Fact]
    public void A_visitor_may_mutate_again_when_their_window_has_passed()
    {
        var budget = Budget(global: 100, perVisitor: 1);
        budget.TrySpend("a", Start, out _);

        Assert.Equal(DemoBudgetDecision.Granted, budget.TrySpend("a", Start.AddSeconds(61), out _));
    }

    /// <summary>Another visitor is unaffected by one visitor's rate.</summary>
    [Fact]
    public void One_visitors_rate_does_not_limit_another()
    {
        var budget = Budget(global: 100, perVisitor: 1);
        budget.TrySpend("a", Start, out _);

        Assert.Equal(DemoBudgetDecision.Granted, budget.TrySpend("b", Start, out _));
    }

    /// <summary>When the whole budget is spent every visitor is refused, and nothing more is spent.</summary>
    [Fact]
    public void An_exhausted_global_budget_refuses_every_visitor()
    {
        var budget = Budget(global: 2, perVisitor: 2);
        budget.TrySpend("a", Start, out _);
        budget.TrySpend("b", Start, out _);

        Assert.Equal(DemoBudgetDecision.GlobalExhausted, budget.TrySpend("c", Start, out _));
        Assert.Equal(DemoBudgetDecision.GlobalExhausted, budget.TrySpend("a", Start.AddSeconds(61), out _));
        Assert.Equal(2, budget.Spent);
    }

    /// <summary>A visitor spamming past their rate burns none of the global budget.</summary>
    [Fact]
    public void A_visitor_over_their_rate_does_not_burn_the_global_budget()
    {
        var budget = Budget(global: 3, perVisitor: 1);
        budget.TrySpend("a", Start, out _);
        for (var i = 0; i < 50; i++)
        {
            budget.TrySpend("a", Start, out _);
        }

        Assert.Equal(1, budget.Spent);
        Assert.Equal(DemoBudgetDecision.Granted, budget.TrySpend("b", Start, out _));
    }

    /// <summary>The visitor table is bounded: visitors beyond it share one overflow bucket.</summary>
    [Fact]
    public void The_visitor_table_never_grows_past_its_bound_plus_the_overflow_bucket()
    {
        var budget = Budget(global: 1000, perVisitor: 5, tracked: 3);

        for (var i = 0; i < 50; i++)
        {
            budget.TrySpend($"visitor-{i}", Start, out _);
        }

        Assert.Equal(4, budget.TrackedVisitors);
    }

    /// <summary>Expired windows are dropped to make room, so a quiet table does not fill with the dead.</summary>
    [Fact]
    public void Expired_windows_make_room_for_new_visitors()
    {
        var budget = Budget(global: 1000, perVisitor: 5, tracked: 2);
        budget.TrySpend("a", Start, out _);
        budget.TrySpend("b", Start, out _);

        budget.TrySpend("c", Start.AddSeconds(120), out _);

        Assert.Equal(1, budget.TrackedVisitors);
    }
}
