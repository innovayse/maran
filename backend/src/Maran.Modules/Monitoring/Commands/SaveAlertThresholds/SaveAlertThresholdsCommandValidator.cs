using FluentValidation;
using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Resources;

namespace Maran.Modules.Monitoring.Commands.SaveAlertThresholds;

/// <summary>Holds every threshold to the bounds <see cref="AlertThresholds"/> keeps.</summary>
/// <remarks>
/// The entity is the rule and this is the 400: the same bounds, answered with a code instead of an
/// exception. The message carries the same code, because the host reads the code of a failure from its
/// message. The processor and the memory may be <c>null</c> (not watched); the disk may not.
/// </remarks>
public sealed class SaveAlertThresholdsCommandValidator : AbstractValidator<SaveAlertThresholdsCommand>
{
    /// <summary>Declares the rules.</summary>
    public SaveAlertThresholdsCommandValidator()
    {
        RuleFor(command => command.DiskPercent)
            .Must(AlertThresholds.IsWithinBounds)
            .WithErrorCode(nameof(ErrorMessages.AlertThresholdOutOfRange))
            .WithMessage(nameof(ErrorMessages.AlertThresholdOutOfRange));

        RuleFor(command => command.CpuPercent)
            .Must(percent =>
            {
                return percent is null || AlertThresholds.IsWithinBounds(percent.Value);
            })
            .WithErrorCode(nameof(ErrorMessages.AlertThresholdOutOfRange))
            .WithMessage(nameof(ErrorMessages.AlertThresholdOutOfRange));

        RuleFor(command => command.MemoryPercent)
            .Must(percent =>
            {
                return percent is null || AlertThresholds.IsWithinBounds(percent.Value);
            })
            .WithErrorCode(nameof(ErrorMessages.AlertThresholdOutOfRange))
            .WithMessage(nameof(ErrorMessages.AlertThresholdOutOfRange));
    }
}
