using FluentValidation;
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
    /// <summary>Two-component PHP version as the packages name it, e.g. <c>8.3</c>.</summary>
    /// <remarks>
    /// Anchored with <c>\z</c>, not <c>$</c>: in .NET <c>$</c> also matches before a trailing
    /// newline, and this value names a php-fpm pool directory in a rendered config.
    /// </remarks>
    private const string PhpVersionPattern = @"\A[0-9]\.[0-9]{1,2}\z";

    /// <summary>Configures the field rules for <see cref="ChangeSitePhpVersionCommand"/>.</summary>
    public ChangeSitePhpVersionCommandValidator()
    {
        RuleFor(command => command.SiteId)
            .NotEmpty();

        RuleFor(command => command.PhpVersion)
            .NotEmpty()
            .Matches(PhpVersionPattern)
            .WithMessage(nameof(ErrorMessages.PhpVersionInvalidFormat));
    }
}
