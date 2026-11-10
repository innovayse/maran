using FluentValidation;
using Maran.Modules.Marketplace.Domain.Policies;
using Maran.Modules.Marketplace.Resources;

namespace Maran.Modules.Marketplace.Commands.RollbackModule;

/// <summary>
/// Validates <see cref="RollbackModuleCommand"/>: the route's module id is well formed. The password is not
/// checked here, because its refusal is audited and counted by the handler and a validation error would
/// leave no trace.
/// </summary>
public sealed class RollbackModuleCommandValidator : AbstractValidator<RollbackModuleCommand>
{
    /// <summary>Creates the validator.</summary>
    public RollbackModuleCommandValidator()
    {
        RuleFor(command => command.Id)
            .Must(ModuleIdPolicy.IsValid)
            .WithMessage(nameof(ErrorMessages.ModuleIdInvalid));
    }
}
