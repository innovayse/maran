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
        // The query string binds a number to an enum without checking it names a member, so
        // ?status=7 would otherwise arrive as a filter no row can match.
        RuleFor(query => query.Status)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.CertificateStatusUnknown))
            .WithMessage(nameof(ErrorMessages.CertificateStatusUnknown));
    }
}
