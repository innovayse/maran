using Maran.Modules.Accounts.Commands.ChangeAccountPlan;

namespace Maran.Modules.Accounts.Tests.Commands.ChangeAccountPlan;

/// <summary>Behavioral contract of <see cref="ChangeAccountPlanCommandValidator"/>.</summary>
public sealed class ChangeAccountPlanCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ChangeAccountPlanCommandValidator _validator = new();

    /// <summary>A move naming a plan is accepted.</summary>
    [Fact]
    public void A_move_naming_a_plan_is_accepted()
    {
        Assert.True(_validator.Validate(new ChangeAccountPlanCommand(Guid.NewGuid())).IsValid);
    }

    /// <summary>A move naming no plan is refused on the plan field.</summary>
    [Fact]
    public void A_move_naming_no_plan_is_refused_on_the_plan_field()
    {
        var result = _validator.Validate(new ChangeAccountPlanCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
        {
            return error.PropertyName == nameof(ChangeAccountPlanCommand.PlanId);
        });
    }
}
