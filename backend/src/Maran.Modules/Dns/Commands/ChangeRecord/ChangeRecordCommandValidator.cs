using FluentValidation;
using Maran.Modules.Dns.Validators;

namespace Maran.Modules.Dns.Commands.ChangeRecord;

/// <summary>Validates <see cref="ChangeRecordCommand"/>: the record must be well-formed for its type.</summary>
public sealed class ChangeRecordCommandValidator : AbstractValidator<ChangeRecordCommand>
{
    /// <summary>Declares the rule: a record is stated, and <see cref="RecordDraftValidator"/> weighs it.</summary>
    public ChangeRecordCommandValidator()
    {
        RuleFor(command => command.Record)
            .NotNull()
            .SetValidator(new RecordDraftValidator());
    }
}
