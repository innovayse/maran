using FluentValidation;
using Maran.Modules.Sites.Resources;

namespace Maran.Modules.Sites.Commands.InstallPhpVersion;

/// <summary>
/// Refuses anything that is not a PHP version number before the agent is asked.
/// </summary>
/// <remarks>
/// The same pattern <c>ChangeSitePhpVersionCommandValidator</c> uses, and deliberately the same
/// string rather than a looser one: a version accepted here and refused there would let an operator
/// install a runtime no site could then be pointed at. The agent validates again on its own side —
/// this is the panel refusing early, not the boundary that matters (rules/security.md).
/// </remarks>
public sealed class InstallPhpVersionCommandValidator : AbstractValidator<InstallPhpVersionCommand>
{
    /// <summary>One digit, a dot, one or two digits: the whole shape of a PHP version line.</summary>
    private const string PhpVersionPattern = @"\A[0-9]\.[0-9]{1,2}\z";

    /// <summary>Creates the validator.</summary>
    public InstallPhpVersionCommandValidator()
    {
        RuleFor(command => command.Version)
            .NotEmpty()
            .Matches(PhpVersionPattern)
            .WithMessage(nameof(ErrorMessages.PhpVersionInvalidFormat));
    }
}
