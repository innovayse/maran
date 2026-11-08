using FluentValidation;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Resources;

namespace Maran.Modules.Sites.Commands.SetPhpExtension;

/// <summary>
/// Refuses a version that is not a PHP version number and an extension name outside the catalogue,
/// before a task is begun or the agent is asked.
/// </summary>
/// <remarks>
/// The version uses the same pattern every other validator in the module does. The extension is held
/// to the panel's copy of the closed catalogue; the agent holds the authoritative one and refuses a
/// name this copy would let through, so this is the panel turning a typed or replayed request away
/// early, not the boundary that matters (rules/security.md). Messages are bare resx keys, as in the
/// sibling validators.
/// </remarks>
public sealed class SetPhpExtensionCommandValidator : AbstractValidator<SetPhpExtensionCommand>
{
    /// <summary>Creates the validator.</summary>
    public SetPhpExtensionCommandValidator()
    {
        RuleFor(command => command.Version)
            .NotEmpty()
            .Matches(PhpVersionFormat.Pattern)
            .WithMessage(nameof(ErrorMessages.PhpVersionInvalidFormat));

        RuleFor(command => command.Extension)
            .Must(PhpExtensionCatalogue.IsKnown)
            .WithMessage(nameof(ErrorMessages.PhpExtensionNotAllowed));
    }
}
