using FluentValidation;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Validators;

namespace Maran.Modules.Accounts.Commands.ChangeResellerEnvelope;

/// <summary>Validates <see cref="ChangeResellerEnvelopeCommand"/> before it reaches the handler (rules/security.md "Input").</summary>
public sealed class ChangeResellerEnvelopeCommandValidator : AbstractValidator<ChangeResellerEnvelopeCommand>
{
    /// <summary>Configures the rules: the envelope is required and held to <see cref="ResellerEnvelopeDtoValidator"/>.</summary>
    public ChangeResellerEnvelopeCommandValidator()
    {
        RuleFor(command => command.Envelope)
            .NotNull()
            .WithMessage(nameof(ErrorMessages.ResellerEnvelopeInvalid))
            .SetValidator(new ResellerEnvelopeDtoValidator());
    }
}
