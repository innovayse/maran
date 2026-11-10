using FluentValidation;
using Maran.Modules.Ssl.Resources;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Ssl.Queries.GetIssuanceOptions;

/// <summary>
/// Validates <see cref="GetIssuanceOptionsQuery"/> before it reaches the handler (rules/security.md "Input").
/// </summary>
/// <remarks>
/// Shape only, with the same message key the issuance command uses. Nothing is ordered here, so the reserved
/// names are not asked: a reserved name simply has no site of the caller's, and the handler answers that as
/// not found.
/// </remarks>
public sealed class GetIssuanceOptionsQueryValidator : AbstractValidator<GetIssuanceOptionsQuery>
{
    /// <summary>Configures the field rules for <see cref="GetIssuanceOptionsQuery"/>.</summary>
    public GetIssuanceOptionsQueryValidator()
    {
        RuleFor(query => query.Domain)
            .NotEmpty()
            .MaximumLength(HostNameRule.MaximumLength)
            .Must(HostNameRule.IsHostName)
            .WithMessage(nameof(ErrorMessages.CertificateDomainInvalidFormat));
    }
}
