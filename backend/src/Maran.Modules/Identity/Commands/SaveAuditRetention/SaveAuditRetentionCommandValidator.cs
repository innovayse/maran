using FluentValidation;
using Maran.Modules.Identity.Domain.Entities;

namespace Maran.Modules.Identity.Commands.SaveAuditRetention;

/// <summary>
/// Bounds the audit retention window. The floor exists because a window of a few days would let one
/// careless or hostile save make the next daily run destroy nearly the whole security record, and the
/// ceiling because a number beyond it is "keep indefinitely" spelled badly, which has its own value.
/// </summary>
/// <remarks>
/// The bounds are the entity's own constants, so the validator and <see cref="AuditRetentionPolicy.Replace"/>
/// cannot disagree; the entity refuses an out-of-range value as well, because a command dispatched on
/// the bus never meets this validator.
/// </remarks>
public sealed class SaveAuditRetentionCommandValidator : AbstractValidator<SaveAuditRetentionCommand>
{
    /// <summary>Configures the field rules for <see cref="SaveAuditRetentionCommand"/>.</summary>
    public SaveAuditRetentionCommandValidator()
    {
        RuleFor(command => command.RetentionDays)
            .Must(days =>
            {
                return AuditRetentionPolicy.IsPermitted(days);
            })
            .WithMessage(nameof(Resources.ErrorMessages.AuditRetentionOutOfRange));
    }
}
