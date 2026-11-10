using FluentValidation;

namespace Maran.Modules.ServerMigration.Commands.StartAccountImport;

/// <summary>Checks the shape of a start request; whether the confirmations are right is the handler's, against the stored import.</summary>
public sealed class StartAccountImportCommandValidator : AbstractValidator<StartAccountImportCommand>
{
    /// <summary>Declares the rules.</summary>
    public StartAccountImportCommandValidator()
    {
        RuleFor(command => command.PlanId).NotEmpty();
        RuleFor(command => command.ConfirmAccountName).NotEmpty().MaximumLength(253);
        RuleFor(command => command.ConfirmKeyFingerprint).NotEmpty().MaximumLength(64);
    }
}
