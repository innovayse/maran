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
        // Over HTTP, ?status=7 never reaches this rule: MVC's binder refuses a number naming no
        // member, answered as RequestParameterInvalid. The rule holds the query to its members for
        // a caller that dispatches it on the bus without HTTP binding.
        RuleFor(query => query.BackendType)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.SiteBackendTypeUnknown))
            .WithMessage(nameof(ErrorMessages.SiteBackendTypeUnknown));

        RuleFor(query => query.Status)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.SiteStatusUnknown))
            .WithMessage(nameof(ErrorMessages.SiteStatusUnknown));

        // None means "every version" and is the type's own state, so only a value is checked: it must be
        // something the column could hold.
        RuleFor(query => query.PhpVersion.Value)
            .Cascade(CascadeMode.Stop)
            .Matches(PhpVersionFormat.Pattern)
            .WithErrorCode(nameof(ErrorMessages.PhpVersionInvalidFormat))
            .WithMessage(nameof(ErrorMessages.PhpVersionInvalidFormat))
            .OverridePropertyName(nameof(ListSitesQuery.PhpVersion))
            .When(query =>
            {
                return !query.PhpVersion.IsNone;
            });
    }
}
