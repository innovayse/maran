using FluentValidation;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Resources;
using Maran.Modules.Dns.Validators;

namespace Maran.Modules.Dns.Commands.ReplaceRecords;

/// <summary>Validates <see cref="ReplaceRecordsCommand"/>: a bounded list of well-formed records.</summary>
public sealed class ReplaceRecordsCommandValidator : AbstractValidator<ReplaceRecordsCommand>
{
    /// <summary>Declares the rules: a list no longer than any zone may hold, each record well-formed.</summary>
    public ReplaceRecordsCommandValidator()
    {
        RuleFor(command => command.Records)
            .NotNull()
            .Must(records =>
            {
                return records is null || records.Count <= DnsLimits.MaxRecordsPerZoneCeiling;
            })
            .WithMessage(nameof(ErrorMessages.ZoneRecordLimitReached));

        RuleForEach(command => command.Records)
            .NotNull()
            .SetValidator(new RecordDraftValidator());
    }
}
