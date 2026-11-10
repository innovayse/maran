using FluentValidation;
using Maran.Modules.Dns.Resources;

namespace Maran.Modules.Dns.Commands.FinishZoneSigningWithdrawal;

/// <summary>Validates <see cref="FinishZoneSigningWithdrawalCommand"/>: the DS record's removal must be stated.</summary>
public sealed class FinishZoneSigningWithdrawalCommandValidator : AbstractValidator<FinishZoneSigningWithdrawalCommand>
{
    /// <summary>Declares the rule: the statement is true. A body that omits it reads as false and is refused too.</summary>
    public FinishZoneSigningWithdrawalCommandValidator()
    {
        RuleFor(command => command.DsRecordRemoved)
            .Equal(true)
            .WithMessage(nameof(ErrorMessages.ZoneSigningDsRemovalNotConfirmed));
    }
}
