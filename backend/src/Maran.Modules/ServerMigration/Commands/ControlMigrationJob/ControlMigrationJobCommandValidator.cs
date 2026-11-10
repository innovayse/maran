using FluentValidation;

namespace Maran.Modules.ServerMigration.Commands.ControlMigrationJob;

/// <summary>Validates <see cref="ControlMigrationJobCommand"/> (issue #197, phase 2).</summary>
public sealed class ControlMigrationJobCommandValidator : AbstractValidator<ControlMigrationJobCommand>
{
    /// <summary>Declares the rules.</summary>
    public ControlMigrationJobCommandValidator()
    {
        RuleFor(command => command.Action).IsInEnum();
    }
}
