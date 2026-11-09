using FluentValidation;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.Mail.Queries.ListMailAliases;

/// <summary>
/// Holds <see cref="ListMailAliasesQuery"/> to the panel's paging bounds and to the kinds that exist
/// (rules/security.md "Input").
/// </summary>
/// <remarks>
/// The account filter is a GUID the binder parses or refuses and the domain filter is compared as a
/// parameter, never written into a query, so neither needs a rule beyond the paging contract. The kind is
/// checked here too, because the query is also dispatched on the bus where no binder refuses a number that
/// names no kind.
/// </remarks>
public sealed class ListMailAliasesQueryValidator : PagedQueryValidator<ListMailAliasesQuery, MailAlias>
{
    /// <summary>Declares the paging rules against <see cref="ListMailAliasesSorts.Map"/>, and the kind rule.</summary>
    public ListMailAliasesQueryValidator()
        : base(ListMailAliasesSorts.Map)
    {
        RuleFor(query => query.Kind)
            .IsInEnum()
            .When(query =>
            {
                return query.Kind is not null;
            });
    }
}
