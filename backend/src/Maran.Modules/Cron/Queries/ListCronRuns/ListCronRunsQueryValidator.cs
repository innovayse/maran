using FluentValidation;
using Maran.Modules.Cron.Domain.Entities;
using Maran.Modules.Cron.Resources;
using Maran.Modules.Cron.Validators;
using Maran.Sdk.Validators;

namespace Maran.Modules.Cron.Queries.ListCronRuns;

/// <summary>Holds <see cref="ListCronRunsQuery"/> to the paging bounds, a real entry id and a known outcome.</summary>
public sealed class ListCronRunsQueryValidator : PagedQueryValidator<ListCronRunsQuery, CronRun>
{
    /// <summary>Configures the rules.</summary>
    public ListCronRunsQueryValidator()
        : base(ListCronRunsSorts.Map)
    {
        RuleFor(query => query.AccountId)
            .NotEmpty();

        RuleFor(query => query.EntryId)
            .Matches(CronEntryIdRule.Pattern)
            .WithMessage(nameof(ErrorMessages.CronEntryIdInvalid));

        // Over HTTP, ?outcome=7 never reaches this rule: MVC's binder refuses a value naming no member.
        // The rule holds the query to its members for a caller that dispatches it on the bus.
        RuleFor(query => query.Outcome)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.CronRunOutcomeUnknown))
            .WithMessage(nameof(ErrorMessages.CronRunOutcomeUnknown));
    }
}
