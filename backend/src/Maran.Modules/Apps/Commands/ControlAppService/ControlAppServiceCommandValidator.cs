using FluentValidation;
using Maran.Modules.Apps.Domain.ValueObjects;
using Maran.Modules.Apps.Resources;

namespace Maran.Modules.Apps.Commands.ControlAppService;

/// <summary>
/// Refuses an id outside the app catalogue and an action outside the four the panel offers before the agent
/// is asked (rules/security.md "Input").
/// </summary>
public sealed class ControlAppServiceCommandValidator : AbstractValidator<ControlAppServiceCommand>
{
    /// <summary>Creates the validator.</summary>
    public ControlAppServiceCommandValidator()
    {
        RuleFor(command => command.AppId)
            .Must(text => { return AppId.TryParse(text, out _); })
            .WithErrorCode(nameof(ErrorMessages.AppIdInvalid))
            .WithMessage(nameof(ErrorMessages.AppIdInvalid));

        // Over HTTP a route value naming no member never reaches this rule: MVC's binder refuses it and the
        // answer is RequestParameterInvalid. The rule holds a command dispatched on the bus without HTTP
        // binding to the same four members.
        RuleFor(command => command.Action)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.AppsServiceActionUnknown))
            .WithMessage(nameof(ErrorMessages.AppsServiceActionUnknown));
    }
}
