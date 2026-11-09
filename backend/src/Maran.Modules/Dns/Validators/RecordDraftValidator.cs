using FluentValidation;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Mappers;

namespace Maran.Modules.Dns.Validators;

/// <summary>
/// Holds one record to the shape law of its type at the boundary (rules/security.md "Input"): the same
/// <see cref="RecordShapePolicy"/> the zone and the agent's own validated types agree with. Shared by every
/// operation that takes a record, which is why it is not in one operation's folder.
/// </summary>
/// <remarks>
/// The zone's name is not known here, so the fit of a long owner name under its zone is judged against an
/// empty apex; the handler judges it again against the real one before the zone is changed. Each failure is
/// a bare resx key, which the exception middleware resolves as an error code.
/// </remarks>
public sealed class RecordDraftValidator : AbstractValidator<RecordDraft>
{
    /// <summary>Declares the rule: whatever the policy finds wrong first is the failure.</summary>
    public RecordDraftValidator()
    {
        RuleFor(draft => draft)
            .Custom((draft, context) =>
            {
                var verdict = RecordShapePolicy.Check(RecordShapePolicy.Normalize(draft), string.Empty);
                if (verdict != RecordShapeVerdict.Accepted)
                {
                    context.AddFailure(RecordShapeMessages.KeyOf(verdict));
                }
            });
    }
}
