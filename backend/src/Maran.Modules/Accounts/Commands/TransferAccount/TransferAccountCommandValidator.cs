using FluentValidation;

namespace Maran.Modules.Accounts.Commands.TransferAccount;

/// <summary>
/// Validates <see cref="TransferAccountCommand"/> before it reaches the handler (rules/security.md
/// "Input"). Only the shape is checked here: whether the reseller and the plan exist, whether the plan
/// belongs to that reseller and whether the reseller has room need the database, so they are the
/// handler's to answer.
/// </summary>
public sealed class TransferAccountCommandValidator : AbstractValidator<TransferAccountCommand>
{
    /// <summary>Configures the field rules.</summary>
    public TransferAccountCommandValidator()
    {
        RuleFor(command => command.PlanId).NotEmpty();

        // An all-zero id is never a reseller; null is how the administrator is named.
        RuleFor(command => command.ResellerId).NotEqual(Guid.Empty);
    }
}
