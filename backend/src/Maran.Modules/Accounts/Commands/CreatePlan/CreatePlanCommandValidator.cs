using Maran.Modules.Accounts.Validators;

namespace Maran.Modules.Accounts.Commands.CreatePlan;

/// <summary>
/// Validates <see cref="CreatePlanCommand"/> before it reaches the handler (rules/security.md
/// "Input"). The rules are the ones every plan-stating command shares, written once in
/// <see cref="PlanDefinitionValidator{TCommand}"/>; whether the name is already taken needs the
/// database and the request's language, so it is the handler's to answer.
/// </summary>
public sealed class CreatePlanCommandValidator : PlanDefinitionValidator<CreatePlanCommand>
{
}
