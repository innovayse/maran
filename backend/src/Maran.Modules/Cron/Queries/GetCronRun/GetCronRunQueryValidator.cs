using FluentValidation;
using Maran.Modules.Cron.Resources;
using Maran.Modules.Cron.Validators;

namespace Maran.Modules.Cron.Queries.GetCronRun;

/// <summary>Validates <see cref="GetCronRunQuery"/> before it reaches the handler.</summary>
public sealed class GetCronRunQueryValidator : AbstractValidator<GetCronRunQuery>
{
    /// <summary>Configures the rules.</summary>
    public GetCronRunQueryValidator()
    {
        RuleFor(query => query.AccountId)
            .NotEmpty();

        RuleFor(query => query.EntryId)
            .Matches(CronEntryIdRule.Pattern)
            .WithMessage(nameof(ErrorMessages.CronEntryIdInvalid));
    }
}
