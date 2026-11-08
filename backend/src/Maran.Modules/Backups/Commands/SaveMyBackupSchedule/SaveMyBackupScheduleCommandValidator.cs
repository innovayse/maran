using FluentValidation;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Domain.Policies;
using Maran.Modules.Backups.Resources;

namespace Maran.Modules.Backups.Commands.SaveMyBackupSchedule;

/// <summary>
/// Validates <see cref="SaveMyBackupScheduleCommand"/> before it reaches the handler
/// (rules/security.md "Input").
/// </summary>
/// <remarks>
/// <para>
/// The customer's bounds are <see cref="CustomerBackupLimits"/>; the hour and the weekday rules are
/// the entity's, the same as the administrator's validator states them, so a value the customer form
/// offers is one the cadence can hold. Each message is a bare resx key, not an English sentence
/// (<c>ExceptionMiddleware</c> forwards only an alphanumeric message and resolves it as an error code).
/// </para>
/// <para>
/// The account is not validated here: whether it is the caller's own is the handler's tenant-scoped
/// read, and a refusal there is a 404, not a 400.
/// </para>
/// </remarks>
public sealed class SaveMyBackupScheduleCommandValidator : AbstractValidator<SaveMyBackupScheduleCommand>
{
    /// <summary>Configures the field rules for <see cref="SaveMyBackupScheduleCommand"/>.</summary>
    public SaveMyBackupScheduleCommandValidator()
    {
        RuleFor(command => command.Frequency)
            .Must(CustomerBackupLimits.AllowsFrequency)
            .WithMessage(nameof(ErrorMessages.BackupScheduleCustomerFrequencyNotAllowed));

        RuleFor(command => command.HourUtc)
            .InclusiveBetween(BackupSchedule.MinimumHourUtc, BackupSchedule.MaximumHourUtc)
            .WithMessage(nameof(ErrorMessages.BackupScheduleHourOutOfRange));

        RuleFor(command => command.RetainCount)
            .GreaterThanOrEqualTo(BackupSchedule.MinimumRetainCount)
            .WithMessage(nameof(ErrorMessages.BackupScheduleCustomerRetainTooLow));

        RuleFor(command => command.RetainCount)
            .LessThanOrEqualTo(CustomerBackupLimits.MaximumRetainCount)
            .WithMessage(nameof(ErrorMessages.BackupScheduleCustomerRetainTooHigh));

        // Split rather than chained, for the reason the administrator's validator gives: one field,
        // one message.
        RuleFor(command => command.DayOfWeekUtc)
            .IsInEnum()
            .When(command => { return command.DayOfWeekUtc is not null; });

        RuleFor(command => command.DayOfWeekUtc)
            .NotNull()
            .When(command => { return command.Frequency == BackupFrequency.Weekly; })
            .WithMessage(nameof(ErrorMessages.BackupScheduleDayRequired));

        RuleFor(command => command.DayOfWeekUtc)
            .Null()
            .When(command => { return command.Frequency == BackupFrequency.Daily; })
            .WithMessage(nameof(ErrorMessages.BackupScheduleDayNotAllowed));
    }
}
