using FluentValidation;
using Maran.Modules.Cron.Resources;
using Maran.Modules.Cron.Validators;

namespace Maran.Modules.Cron.Queries.GetCronEntryOutput;

/// <summary>
/// Validates <see cref="GetCronEntryOutputQuery"/> before it reaches the handler (rules/security.md
/// "Input").
/// </summary>
/// <remarks>
/// A query gets a validator here for the same reason the commands do, and with more force: this is
/// the operation whose identifier the agent turns into a path to READ under the account's home, so
/// an id shaped like a path is exactly what both layers exist to refuse.
/// </remarks>
public sealed class GetCronEntryOutputQueryValidator : AbstractValidator<GetCronEntryOutputQuery>
{
    /// <summary>Configures the field rules for <see cref="GetCronEntryOutputQuery"/>.</summary>
    public GetCronEntryOutputQueryValidator()
    {
        RuleFor(query => query.AccountId)
            .NotEmpty();

        RuleFor(query => query.EntryId)
            .Matches(CronEntryIdRule.Pattern)
            .WithMessage(nameof(ErrorMessages.CronEntryIdInvalid));
    }
}
