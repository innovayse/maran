using FluentValidation;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Resources;

namespace Maran.Modules.Backups.Commands.SaveBackupSchedule;

/// <summary>
/// Validates <see cref="SaveBackupScheduleCommand"/> before it reaches the handler
/// (rules/security.md "Input").
/// </summary>
/// <remarks>
/// <para>
/// The bounds are the entity's own constants rather than numbers repeated here, so a ceiling can
/// only be changed in one place. What this validator cannot decide is whether the named account
/// exists — that is the handler's tenant-scoped read.
/// </para>
/// <para>
/// <b>The weekday rule is stated in both directions.</b> A weekly schedule with no weekday would
/// have no day to run on, and a daily schedule carrying one would show an operator a weekday the
/// pass ignores — a form that accepts a value nothing reads is how a screen comes to lie about what
/// the server will do.
/// </para>
/// <para>
/// Each message is a bare resx key, not an English sentence: <c>ExceptionMiddleware</c> forwards a
/// validation message only when it is entirely alphanumeric and then resolves it as an error code
/// against this module's resources; an English sentence is silently discarded and the caller gets
/// the generic failure instead.
/// </para>
/// </remarks>
public sealed class SaveBackupScheduleCommandValidator : AbstractValidator<SaveBackupScheduleCommand>
{
    /// <summary>Configures the field rules for <see cref="SaveBackupScheduleCommand"/>.</summary>
    public SaveBackupScheduleCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEqual(Guid.Empty)
            .When(command => { return command.AccountId is not null; });

        RuleFor(command => command.Frequency)
            .IsInEnum();

        RuleFor(command => command.HourUtc)
            .InclusiveBetween(BackupSchedule.MinimumHourUtc, BackupSchedule.MaximumHourUtc)
            .WithMessage(nameof(ErrorMessages.BackupScheduleHourOutOfRange));

        RuleFor(command => command.RetainCount)
            .InclusiveBetween(BackupSchedule.MinimumRetainCount, BackupSchedule.MaximumRetainCount)
            .WithMessage(nameof(ErrorMessages.BackupScheduleRetainOutOfRange));

        // Split from the two rules below rather than chained onto them: chained, a weekly schedule
        // with no weekday fails NotNull AND IsInEnum, and the caller is handed two messages for one
        // field where the second one is meaningless.
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
