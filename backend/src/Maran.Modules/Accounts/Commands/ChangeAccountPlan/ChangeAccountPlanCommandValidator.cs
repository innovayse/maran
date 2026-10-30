using FluentValidation;

namespace Maran.Modules.Accounts.Commands.ChangeAccountPlan;

/// <summary>
/// Validates <see cref="ChangeAccountPlanCommand"/> before it reaches the handler (rules/security.md
/// "Input"). Only the shape is checked here: whether the plan exists and whether the account fits it
/// need the database and the agent, so they are the handler's to answer.
/// </summary>
public sealed class ChangeAccountPlanCommandValidator : AbstractValidator<ChangeAccountPlanCommand>
{
    /// <summary>Configures the field rules.</summary>
    public ChangeAccountPlanCommandValidator()
    {
        RuleFor(command => command.PlanId).NotEmpty();
    }
}
