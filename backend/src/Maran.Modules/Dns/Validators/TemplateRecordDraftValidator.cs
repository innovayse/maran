using FluentValidation;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Mappers;

namespace Maran.Modules.Dns.Validators;

/// <summary>
/// Holds one template record to <see cref="TemplateRecordRules"/> at the boundary (rules/security.md "Input"):
/// the closed variables, then the shape law of a record, applied to what the record expands to under the sample
/// context. The handler judges the same record again, and the records against each other.
/// </summary>
/// <remarks>Each failure is a bare resx key, which the exception middleware resolves as an error code.</remarks>
public sealed class TemplateRecordDraftValidator : AbstractValidator<TemplateRecordDraft>
{
    /// <summary>Declares the rule: whatever the policy finds wrong first is the failure.</summary>
    public TemplateRecordDraftValidator()
    {
        RuleFor(draft => draft)
            .Custom((draft, context) =>
            {
                var verdict = TemplateRecordRules.Check(TemplateExpansionPolicy.Normalize(draft));
                if (!verdict.IsAccepted)
                {
                    context.AddFailure(TemplateRecordMessages.KeyOf(verdict));
                }
            });
    }
}
