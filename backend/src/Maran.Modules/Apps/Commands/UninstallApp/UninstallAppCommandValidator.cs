using FluentValidation;
using Maran.Modules.Apps.Domain.ValueObjects;
using Maran.Modules.Apps.Resources;

namespace Maran.Modules.Apps.Commands.UninstallApp;

/// <summary>
/// Refuses anything that is not a member of the app catalogue before a task is begun or the agent is asked
/// (rules/security.md "Input").
/// </summary>
public sealed class UninstallAppCommandValidator : AbstractValidator<UninstallAppCommand>
{
    /// <summary>Creates the validator.</summary>
    public UninstallAppCommandValidator()
    {
        RuleFor(command => command.AppId)
            .Must(text => { return AppId.TryParse(text, out _); })
            .WithErrorCode(nameof(ErrorMessages.AppIdInvalid))
            .WithMessage(nameof(ErrorMessages.AppIdInvalid));
    }
}
