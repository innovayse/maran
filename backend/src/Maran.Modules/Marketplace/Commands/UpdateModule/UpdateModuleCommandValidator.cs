using FluentValidation;
using Maran.Modules.Marketplace.Domain.Policies;
using Maran.Modules.Marketplace.Resources;
using Maran.SharedKernel.Utilities.Versions;

namespace Maran.Modules.Marketplace.Commands.UpdateModule;

/// <summary>
/// Validates <see cref="UpdateModuleCommand"/>: the route's module id and the version are well formed. The
/// acknowledgement and the password are not checked here, because their refusal is audited by the handler
/// and a validation error leaves no trace.
/// </summary>
public sealed class UpdateModuleCommandValidator : AbstractValidator<UpdateModuleCommand>
{
    /// <summary>Creates the validator.</summary>
    public UpdateModuleCommandValidator()
    {
        RuleFor(command => command.Id)
            .Must(ModuleIdPolicy.IsValid)
            .WithMessage(nameof(ErrorMessages.ModuleIdInvalid));

        RuleFor(command => command.Version)
            .Must(version => { return SemanticVersion.TryParse(version, out _); })
            .WithMessage(nameof(ErrorMessages.ModuleVersionInvalid));
    }
}
