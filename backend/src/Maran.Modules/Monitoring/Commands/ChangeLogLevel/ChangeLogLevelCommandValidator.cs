using FluentValidation;
using Maran.Modules.Monitoring.Resources;

namespace Maran.Modules.Monitoring.Commands.ChangeLogLevel;

/// <summary>Refuses a level that names no member of <see cref="Maran.Sdk.Contracts.PanelLogLevel"/>.</summary>
/// <remarks>
/// Over HTTP an unknown name or number is already refused while the body is read (the panel's
/// binding refusal, 400). The command is also dispatched on the message bus, where nothing binds it,
/// so a cast value that names no member is refused here as the caller's mistake rather than failing
/// in the logger's switch as a bug.
/// </remarks>
public sealed class ChangeLogLevelCommandValidator : AbstractValidator<ChangeLogLevelCommand>
{
    /// <summary>Declares the rule.</summary>
    public ChangeLogLevelCommandValidator()
    {
        RuleFor(command => command.Level)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.LogLevelUnknown));
    }
}
