using FluentValidation;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Resources;
using Maran.Sdk.Validators;

namespace Maran.Modules.Sites.Queries.ListSites;

/// <summary>
/// Holds <see cref="ListSitesQuery"/> to the panel's paging bounds and its own filters
/// (rules/security.md "Input").
/// </summary>
/// <remarks>
/// Each message is a bare resx key, for the reason given on
/// <see cref="Commands.CreateSite.CreateSiteCommandValidator"/>: only a key reaches the customer.
/// </remarks>
public sealed class ListSitesQueryValidator : PagedQueryValidator<ListSitesQuery, Site>
{
    /// <summary>Declares the paging rules against <see cref="ListSitesSorts.Map"/>, then the filters.</summary>
    public ListSitesQueryValidator()
        : base(ListSitesSorts.Map)
    {
        // The query string binds a number to an enum without checking it names a member, so
        // ?status=7 would otherwise arrive as a filter no row can match.
        RuleFor(query => query.BackendType)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.SiteBackendTypeUnknown))
            .WithMessage(nameof(ErrorMessages.SiteBackendTypeUnknown));

        RuleFor(query => query.Status)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.SiteStatusUnknown))
            .WithMessage(nameof(ErrorMessages.SiteStatusUnknown));

        // Empty means "every version"; anything else must be a version a site could carry. Null is
        // refused rather than read as empty: one spelling of "every version".
        RuleFor(query => query.PhpVersion)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithErrorCode(nameof(ErrorMessages.PhpVersionInvalidFormat))
            .WithMessage(nameof(ErrorMessages.PhpVersionInvalidFormat))
            .Matches(PhpVersionFormat.Pattern)
            .When(query =>
            {
                return query.PhpVersion is not null && query.PhpVersion.Length > 0;
            }, ApplyConditionTo.CurrentValidator)
            .WithErrorCode(nameof(ErrorMessages.PhpVersionInvalidFormat))
            .WithMessage(nameof(ErrorMessages.PhpVersionInvalidFormat));
    }
}
