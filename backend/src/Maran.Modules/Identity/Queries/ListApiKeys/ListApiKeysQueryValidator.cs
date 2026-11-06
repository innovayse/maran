using FluentValidation;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Validators;

namespace Maran.Modules.Identity.Queries.ListApiKeys;

/// <summary>
/// Holds <see cref="ListApiKeysQuery"/> to the panel's paging bounds and to the status values the
/// filter knows (rules/security.md "Input").
/// </summary>
/// <remarks>Each message is a bare resx key: only a key reaches the caller, localized by the panel.</remarks>
public sealed class ListApiKeysQueryValidator : PagedQueryValidator<ListApiKeysQuery, ApiKey>
{
    /// <summary>Declares the paging rules against <see cref="ListApiKeysSorts.Map"/>, then the status filter.</summary>
    public ListApiKeysQueryValidator()
        : base(ListApiKeysSorts.Map)
    {
        // None means "every status" and is the type's own state, so only a value is checked: a value
        // outside the three names no row, and is refused rather than answered empty.
        RuleFor(query => query.Status.Value)
            .Must(status =>
            {
                return ApiKeyStatusFilters.All.Contains(status);
            })
            .WithErrorCode(nameof(ErrorMessages.ApiKeyStatusFilterInvalid))
            .WithMessage(nameof(ErrorMessages.ApiKeyStatusFilterInvalid))
            .OverridePropertyName(nameof(ListApiKeysQuery.Status))
            .When(query =>
            {
                return !query.Status.IsNone;
            });
    }
}
