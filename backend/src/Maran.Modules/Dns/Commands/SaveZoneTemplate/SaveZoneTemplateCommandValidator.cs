using FluentValidation;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Resources;
using Maran.Modules.Dns.Validators;

namespace Maran.Modules.Dns.Commands.SaveZoneTemplate;

/// <summary>
/// Validates <see cref="SaveZoneTemplateCommand"/> (rules/security.md "Input"): a name and a description within
/// <see cref="TemplateNamePolicy"/>, a bounded list, and every record held to the template record rules. The cap
/// the operator sets and the records against each other are judged by the handler, which knows the options.
/// </summary>
public sealed class SaveZoneTemplateCommandValidator : AbstractValidator<SaveZoneTemplateCommand>
{
    /// <summary>Declares the field rules for <see cref="SaveZoneTemplateCommand"/>.</summary>
    public SaveZoneTemplateCommandValidator()
    {
        RuleFor(command => command.Name)
            .Must(name =>
            {
                return TemplateNamePolicy.IsNameValid(TemplateNamePolicy.Normalize(name));
            })
            .WithMessage(nameof(ErrorMessages.ZoneTemplateNameInvalid));

        RuleFor(command => command.Description)
            .Must(TemplateNamePolicy.IsDescriptionValid)
            .WithMessage(nameof(ErrorMessages.ZoneTemplateDescriptionInvalid));

        RuleFor(command => command.Records)
            .NotNull()
            .Must(records =>
            {
                return records is null || records.Count <= DnsLimits.MaxRecordsPerZoneCeiling;
            })
            .WithMessage(nameof(ErrorMessages.ZoneTemplateTooManyRecords));

        RuleForEach(command => command.Records)
            .NotNull()
            .SetValidator(new TemplateRecordDraftValidator());
    }
}
