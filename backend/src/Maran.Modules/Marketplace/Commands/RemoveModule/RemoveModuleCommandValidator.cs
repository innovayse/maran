using FluentValidation;
using Maran.Modules.Marketplace.Domain.Policies;
using Maran.Modules.Marketplace.Resources;

namespace Maran.Modules.Marketplace.Commands.RemoveModule;

/// <summary>
/// Validates <see cref="RemoveModuleCommand"/>: the route's module id is well formed. The password is not
/// checked here, because its refusal is audited and counted by the handler and a validation error would
/// leave no trace.
/// </summary>
public sealed class RemoveModuleCommandValidator : AbstractValidator<RemoveModuleCommand>
{
    /// <summary>Creates the validator.</summary>
    public RemoveModuleCommandValidator()
    {
        RuleFor(command => command.Id)
            .Must(ModuleIdPolicy.IsValid)
            .WithMessage(nameof(ErrorMessages.ModuleIdInvalid));
    }
}
