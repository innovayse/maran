using FluentValidation;
using Maran.Modules.Apps.Domain.ValueObjects;
using Maran.Modules.Apps.Resources;

namespace Maran.Modules.Apps.Commands.InstallApp;

/// <summary>
/// Refuses anything that is not a member of the app catalogue before a task is begun or the agent is asked
/// (rules/security.md "Input"). The agent parses the id against its own closed catalogue again; this is the
/// panel refusing early, not the boundary that matters.
/// </summary>
public sealed class InstallAppCommandValidator : AbstractValidator<InstallAppCommand>
{
    /// <summary>Creates the validator.</summary>
    public InstallAppCommandValidator()
    {
        RuleFor(command => command.AppId)
            .Must(text => { return AppId.TryParse(text, out _); })
            .WithErrorCode(nameof(ErrorMessages.AppIdInvalid))
            .WithMessage(nameof(ErrorMessages.AppIdInvalid));
    }
}
