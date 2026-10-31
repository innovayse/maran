using FluentValidation;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Resources;
using Maran.Sdk.Validators;

namespace Maran.Modules.Accounts.Queries.ListAccounts;

/// <summary>
/// Holds <see cref="ListAccountsQuery"/> to the panel's paging bounds and its own filters
/// (rules/security.md "Input").
/// </summary>
public sealed class ListAccountsQueryValidator : PagedQueryValidator<ListAccountsQuery, Account>
{
    /// <summary>Declares the paging rules against <see cref="ListAccountsSorts.Map"/>, then the status filter.</summary>
    public ListAccountsQueryValidator()
        : base(ListAccountsSorts.Map)
    {
        // Over HTTP, ?status=7 never reaches this rule: MVC's binder refuses a number naming no
        // member, answered as RequestParameterInvalid. The rule holds the query to its members for
        // a caller that dispatches it on the bus without HTTP binding.
        RuleFor(query => query.Status)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.AccountStatusUnknown))
            .WithMessage(nameof(ErrorMessages.AccountStatusUnknown));
    }
}
