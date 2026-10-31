using FluentValidation;
using Maran.Modules.Ssl.Domain.Entities;
using Maran.Modules.Ssl.Resources;
using Maran.Sdk.Validators;

namespace Maran.Modules.Ssl.Queries.ListCertificates;

/// <summary>
/// Holds <see cref="ListCertificatesQuery"/> to the panel's paging bounds and its own filters
/// (rules/security.md "Input").
/// </summary>
public sealed class ListCertificatesQueryValidator : PagedQueryValidator<ListCertificatesQuery, Certificate>
{
    /// <summary>Declares the paging rules against <see cref="ListCertificatesSorts.Map"/>, then the status filter.</summary>
    public ListCertificatesQueryValidator()
        : base(ListCertificatesSorts.Map)
    {
        // Over HTTP, ?status=7 never reaches this rule: MVC's binder refuses a number naming no
        // member, answered as RequestParameterInvalid. The rule holds the query to its members for
        // a caller that dispatches it on the bus without HTTP binding.
        RuleFor(query => query.Status)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.CertificateStatusUnknown))
            .WithMessage(nameof(ErrorMessages.CertificateStatusUnknown));
    }
}
