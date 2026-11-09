using FluentValidation;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Resources;
using Maran.Sdk.Validators;

namespace Maran.Modules.Mail.Queries.ListMailboxes;

/// <summary>
/// Holds <see cref="ListMailboxesQuery"/> to the panel's paging bounds and to its own filters
/// (rules/security.md "Input").
/// </summary>
/// <remarks>
/// The status filter needs no rule: over HTTP MVC's binder refuses a value naming no member before this
/// validator runs (answered as <c>RequestParameterInvalid</c>), and a query dispatched on the bus with an
/// undefined status matches no mailbox, which is the honest answer to a status nothing has.
/// </remarks>
public sealed class ListMailboxesQueryValidator : PagedQueryValidator<ListMailboxesQuery, Mailbox>
{
    /// <summary>Declares the paging rules against <see cref="ListMailboxesSorts.Map"/>, and the filter rules.</summary>
    public ListMailboxesQueryValidator()
        : base(ListMailboxesSorts.Map)
    {
        // None means "every domain" and is the type's own state, so only a value is checked: it must be
        // something a domain name column could hold.
        RuleFor(query => query.Domain.Value)
            .MaximumLength(MailDefaults.DomainMaxLength)
            .WithErrorCode(nameof(ErrorMessages.MailDomainTooLong))
            .WithMessage(nameof(ErrorMessages.MailDomainTooLong))
            .OverridePropertyName(nameof(ListMailboxesQuery.Domain))
            .When(query =>
            {
                return !query.Domain.IsNone;
            });
    }
}
