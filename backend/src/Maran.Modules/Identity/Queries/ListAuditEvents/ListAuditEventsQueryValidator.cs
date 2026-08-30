using FluentValidation;

namespace Maran.Modules.Identity.Queries.ListAuditEvents;

/// <summary>Bounds the page size of <see cref="ListAuditEventsQuery"/>.</summary>
public sealed class ListAuditEventsQueryValidator : AbstractValidator<ListAuditEventsQuery>
{
    /// <summary>Largest page the journal will return in one call.</summary>
    private const int MaxLimit = 500;

    /// <summary>Configures the field rules for <see cref="ListAuditEventsQuery"/>.</summary>
    public ListAuditEventsQueryValidator()
    {
        // The upper bound is the point: an unbounded read of a table that only grows is a denial
        // of service an administrator can trigger by accident, on the one screen that must stay
        // usable while something is going wrong.
        RuleFor(query => query.Limit).InclusiveBetween(1, MaxLimit);
    }
}
