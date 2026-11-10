using FluentValidation;

namespace Maran.Modules.ServerMigration.Commands.ControlMigrationAccount;

/// <summary>Validates <see cref="ControlMigrationAccountCommand"/> (issue #197, phase 2).</summary>
public sealed class ControlMigrationAccountCommandValidator : AbstractValidator<ControlMigrationAccountCommand>
{
    /// <summary>Declares the rules.</summary>
    public ControlMigrationAccountCommandValidator()
    {
        RuleFor(command => command.Action).IsInEnum();
    }
}
