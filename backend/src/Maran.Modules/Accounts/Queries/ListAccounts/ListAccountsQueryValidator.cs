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
        // The query string binds a number to an enum without checking it names a member, so
        // ?status=7 would otherwise arrive as a filter no row can match.
        RuleFor(query => query.Status)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.AccountStatusUnknown))
            .WithMessage(nameof(ErrorMessages.AccountStatusUnknown));
    }
}
