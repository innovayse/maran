using FluentValidation;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Resources;

namespace Maran.Modules.Sites.Commands.ChangeSitePhpVersion;

/// <summary>
/// Validates <see cref="ChangeSitePhpVersionCommand"/> before it reaches the handler. Only the
/// SHAPE of the version is checked here; whether the host has it is answered by the agent's
/// installed list as <c>PhpVersionNotInstalled</c>, and the agent re-validates it again regardless.
/// </summary>
/// <remarks>
/// The message is a bare resx key rather than an English sentence, for the reason spelled out on
/// <see cref="CreateSite.CreateSiteCommandValidator"/>: a sentence never reaches the customer.
/// </remarks>
public sealed class ChangeSitePhpVersionCommandValidator : AbstractValidator<ChangeSitePhpVersionCommand>
{
    /// <summary>Configures the field rules for <see cref="ChangeSitePhpVersionCommand"/>.</summary>
    public ChangeSitePhpVersionCommandValidator()
    {
        RuleFor(command => command.SiteId)
            .NotEmpty();

        RuleFor(command => command.PhpVersion)
            .NotEmpty()
            .Matches(PhpVersionFormat.Pattern)
            .WithMessage(nameof(ErrorMessages.PhpVersionInvalidFormat));
    }
}
