using FluentValidation;
using Maran.Modules.Monitoring.Resources;

namespace Maran.Modules.Monitoring.Commands.ChangeLogLevel;

/// <summary>Refuses a level that names no member of <see cref="Maran.Sdk.Contracts.PanelLogLevel"/>.</summary>
/// <remarks>
/// The panel's JSON reads an enum from its name and, by default, from a number as well, so a body of
/// <c>{"level": 99}</c> produces a typed value that is no level at all. Left through, it would reach
/// the logger's switch and fail there as a bug instead of being refused here as the caller's mistake.
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
