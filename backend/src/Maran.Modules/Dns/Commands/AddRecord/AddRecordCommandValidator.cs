using FluentValidation;
using Maran.Modules.Dns.Validators;

namespace Maran.Modules.Dns.Commands.AddRecord;

/// <summary>Validates <see cref="AddRecordCommand"/>: the record must be well-formed for its type.</summary>
public sealed class AddRecordCommandValidator : AbstractValidator<AddRecordCommand>
{
    /// <summary>Declares the rule: a record is stated, and <see cref="RecordDraftValidator"/> weighs it.</summary>
    public AddRecordCommandValidator()
    {
        RuleFor(command => command.Record)
            .NotNull()
            .SetValidator(new RecordDraftValidator());
    }
}
