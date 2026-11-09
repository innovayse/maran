using FluentValidation;
using Maran.Modules.Databases.Domain.Policies;
using Maran.Modules.Databases.Resources;

namespace Maran.Modules.Databases.Commands.ChangeDatabaseUserPrivileges;

/// <summary>Validates <see cref="ChangeDatabaseUserPrivilegesCommand"/> before it reaches the handler.</summary>
/// <remarks>
/// The agent refuses the same things again: a duplicate, an unspecified value and an unknown number are
/// invalid input there too, so this never substitutes for its boundary check.
/// </remarks>
public sealed class ChangeDatabaseUserPrivilegesCommandValidator : AbstractValidator<ChangeDatabaseUserPrivilegesCommand>
{
    /// <summary>Configures the rules for <see cref="ChangeDatabaseUserPrivilegesCommand"/>.</summary>
    public ChangeDatabaseUserPrivilegesCommandValidator()
    {
        RuleFor(command => command.Privileges)
            .NotNull()
            .WithMessage(nameof(ErrorMessages.DatabasePrivilegesInvalid));

        RuleForEach(command => command.Privileges)
            .IsInEnum()
            .WithMessage(nameof(ErrorMessages.DatabasePrivilegesInvalid));

        RuleFor(command => command.Privileges)
            .Must(privileges =>
            {
                return privileges is null
                    || (privileges.Count <= DatabasePrivilegeSets.All.Count && privileges.Distinct().Count() == privileges.Count);
            })
            .WithMessage(nameof(ErrorMessages.DatabasePrivilegesInvalid));
    }
}
