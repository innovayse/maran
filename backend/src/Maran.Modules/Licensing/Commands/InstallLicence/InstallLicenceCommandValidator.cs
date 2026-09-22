using FluentValidation;
using Maran.Modules.Licensing.Resources;

namespace Maran.Modules.Licensing.Commands.InstallLicence;

/// <summary>
/// Validates <see cref="InstallLicenceCommand"/> before it reaches the handler
/// (rules/security.md "Input").
/// </summary>
/// <remarks>
/// Only the request-shape check belongs here — an empty body is a malformed HTTP request, not a
/// licence that failed to verify. Every other rejection (unparsable JSON, bad signature, expired,
/// wrong product) is a fact about the ARTEFACT, decided by
/// <c>Services.LicenceVerifier.VerifyAsync</c> inside the handler, and reported through one of the
/// five <c>LicenceInstall*</c> codes in <c>Resources/ErrorMessages.resx</c> — never through this
/// validator, which would collapse those distinct, actionable reasons into one generic "validation
/// failed" the caller cannot act on.
/// </remarks>
public sealed class InstallLicenceCommandValidator : AbstractValidator<InstallLicenceCommand>
{
    /// <summary>Builds the rule set.</summary>
    public InstallLicenceCommandValidator()
    {
        RuleFor(command => command.RawLicenceText)
            .NotEmpty()
            .WithMessage(nameof(ErrorMessages.LicenceInstallTextRequired));
    }
}
