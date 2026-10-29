using Maran.Modules.Accounts.Validators;

namespace Maran.Modules.Accounts.Commands.UpdatePlan;

/// <summary>
/// Validates <see cref="UpdatePlanCommand"/> before it reaches the handler (rules/security.md
/// "Input"). The rules are the ones every plan-stating command shares, written once in
/// <see cref="PlanDefinitionValidator{TCommand}"/>; whether the plan exists, whether the name is
/// taken and whether a lowered quota fits what accounts already hold all need the database or the
/// agent, so they are the handler's to answer.
/// </summary>
public sealed class UpdatePlanCommandValidator : PlanDefinitionValidator<UpdatePlanCommand>
{
}
