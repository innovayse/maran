using Maran.Modules.Accounts.Commands.TransferAccount;

namespace Maran.Modules.Accounts.Tests.Commands.TransferAccount;

/// <summary>Behavioral contract of <see cref="TransferAccountCommandValidator"/>.</summary>
public sealed class TransferAccountCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly TransferAccountCommandValidator _validator = new();

    /// <summary>A move to a reseller naming a plan is accepted.</summary>
    [Fact]
    public void A_move_to_a_reseller_naming_a_plan_is_accepted()
    {
        Assert.True(_validator.Validate(new TransferAccountCommand(Guid.NewGuid(), Guid.NewGuid())).IsValid);
    }

    /// <summary>A move to the administrator, named by no reseller, is accepted.</summary>
    [Fact]
    public void A_move_to_the_administrator_named_by_no_reseller_is_accepted()
    {
        Assert.True(_validator.Validate(new TransferAccountCommand(null, Guid.NewGuid())).IsValid);
    }

    /// <summary>A move naming no plan is refused on the plan field.</summary>
    [Fact]
    public void A_move_naming_no_plan_is_refused_on_the_plan_field()
    {
        var result = _validator.Validate(new TransferAccountCommand(Guid.NewGuid(), Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
        {
            return error.PropertyName == nameof(TransferAccountCommand.PlanId);
        });
    }

    /// <summary>An all-zero reseller id is refused on the reseller field, because null is how the administrator is named.</summary>
    [Fact]
    public void An_all_zero_reseller_id_is_refused_on_the_reseller_field()
    {
        var result = _validator.Validate(new TransferAccountCommand(Guid.Empty, Guid.NewGuid()));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
        {
            return error.PropertyName == nameof(TransferAccountCommand.ResellerId);
        });
    }
}
