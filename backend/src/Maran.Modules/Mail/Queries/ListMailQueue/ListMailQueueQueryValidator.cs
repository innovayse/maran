using Maran.Modules.Mail.Common;
using Maran.Sdk.Validators;

namespace Maran.Modules.Mail.Queries.ListMailQueue;

/// <summary>
/// Holds <see cref="ListMailQueueQuery"/> to the panel's paging bounds (rules/security.md "Input").
/// </summary>
/// <remarks>
/// The search is held to its bounds by its binder and compared in memory, never written into a query,
/// so no rule is needed beyond the paging contract.
/// </remarks>
public sealed class ListMailQueueQueryValidator : PagedQueryValidator<ListMailQueueQuery, MailQueueEntryDto>
{
    /// <summary>Declares the paging rules against <see cref="ListMailQueueSorts.Map"/>.</summary>
    public ListMailQueueQueryValidator()
        : base(ListMailQueueSorts.Map)
    {
    }
}
