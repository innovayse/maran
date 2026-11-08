using Maran.Host.Demo.Options;

namespace Maran.Host.Demo.Tests.Options;

/// <summary>The relation between the budgets is held at start-up.</summary>
public sealed class DemoOptionsValidatorTests
{
    /// <summary>The defaults are valid.</summary>
    [Fact]
    public void The_defaults_are_valid()
    {
        Assert.True(new DemoOptionsValidator().Validate(null, new DemoOptions()).Succeeded);
    }

    /// <summary>One visitor may not be granted more than the whole demo has.</summary>
    [Fact]
    public void A_visitor_budget_above_the_global_budget_is_refused()
    {
        var options = new DemoOptions { GlobalMutationBudget = 10, VisitorMutationsPerWindow = 11 };

        var result = new DemoOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
    }

    /// <summary>A visitor budget equal to the global one is allowed.</summary>
    [Fact]
    public void A_visitor_budget_equal_to_the_global_budget_is_valid()
    {
        var options = new DemoOptions { GlobalMutationBudget = 10, VisitorMutationsPerWindow = 10 };

        Assert.True(new DemoOptionsValidator().Validate(null, options).Succeeded);
    }

    /// <summary>One visitor may not be granted more sign-ins than the whole demo accepts.</summary>
    [Fact]
    public void A_visitor_sign_in_allowance_above_the_global_one_is_refused()
    {
        var options = new DemoOptions { GlobalSignInsPerWindow = 10, VisitorSignInsPerWindow = 11 };

        Assert.True(new DemoOptionsValidator().Validate(null, options).Failed);
    }
}
