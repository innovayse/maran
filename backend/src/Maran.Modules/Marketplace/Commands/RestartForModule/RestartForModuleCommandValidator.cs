using FluentValidation;
using Maran.Modules.Marketplace.Domain.Policies;
using Maran.Modules.Marketplace.Resources;

namespace Maran.Modules.Marketplace.Commands.RestartForModule;

/// <summary>Validates <see cref="RestartForModuleCommand"/>: the route's module id is well formed.</summary>
public sealed class RestartForModuleCommandValidator : AbstractValidator<RestartForModuleCommand>
{
    /// <summary>Creates the validator.</summary>
    public RestartForModuleCommandValidator()
    {
        RuleFor(command => command.Id)
            .Must(ModuleIdPolicy.IsValid)
            .WithMessage(nameof(ErrorMessages.ModuleIdInvalid));
    }
}
