using FluentValidation;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Resources;
using Maran.Sdk.Validators;

namespace Maran.Modules.Accounts.Queries.ListResellers;

/// <summary>
/// Holds <see cref="ListResellersQuery"/> to the panel's paging bounds and its own status filter
/// (rules/security.md "Input").
/// </summary>
public sealed class ListResellersQueryValidator : PagedQueryValidator<ListResellersQuery, Reseller>
{
    /// <summary>Declares the paging rules against <see cref="ListResellersSorts.Map"/>, then the status filter.</summary>
    public ListResellersQueryValidator()
        : base(ListResellersSorts.Map)
    {
        // Over HTTP, ?status=7 never reaches this rule: MVC's binder refuses a number naming no member,
        // answered as RequestParameterInvalid. The rule holds the query to its members for a caller that
        // dispatches it on the bus without HTTP binding. The reseller state shares the account state's
        // wording: the same two words, the same sentence.
        RuleFor(query => query.Status)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.AccountStatusUnknown))
            .WithMessage(nameof(ErrorMessages.AccountStatusUnknown));
    }
}
