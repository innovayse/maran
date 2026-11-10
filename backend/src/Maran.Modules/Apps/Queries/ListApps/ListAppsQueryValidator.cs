using FluentValidation;
using Maran.Modules.Apps.Models;
using Maran.Modules.Apps.Resources;
using Maran.Sdk.Validators;

namespace Maran.Modules.Apps.Queries.ListApps;

/// <summary>Holds <see cref="ListAppsQuery"/> to the panel's paging bounds and its two filters (rules/security.md "Input").</summary>
public sealed class ListAppsQueryValidator : PagedQueryValidator<ListAppsQuery, AppRow>
{
    /// <summary>Declares the paging rules against <see cref="ListAppsSorts.Map"/>, then the filters.</summary>
    public ListAppsQueryValidator()
        : base(ListAppsSorts.Map)
    {
        // Over HTTP, ?kind=7 never reaches these rules: MVC's binder refuses a number naming no member,
        // answered as RequestParameterInvalid. They hold the query to its members for a caller that
        // dispatches it on the bus without HTTP binding.
        RuleFor(query => query.Kind)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.AppKindUnknown))
            .WithMessage(nameof(ErrorMessages.AppKindUnknown));

        RuleFor(query => query.State)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.AppStateUnknown))
            .WithMessage(nameof(ErrorMessages.AppStateUnknown));
    }
}
