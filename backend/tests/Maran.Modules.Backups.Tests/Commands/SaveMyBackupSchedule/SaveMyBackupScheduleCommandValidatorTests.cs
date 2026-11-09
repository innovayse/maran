using Maran.Modules.Backups.Commands.SaveMyBackupSchedule;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Domain.Policies;

namespace Maran.Modules.Backups.Tests.Commands.SaveMyBackupSchedule;

/// <summary>What the boundary accepts as a customer's own schedule, and what it refuses.</summary>
public sealed class SaveMyBackupScheduleCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly SaveMyBackupScheduleCommandValidator _validator = new();

    /// <summary>A daily schedule at a real hour keeping a few copies is accepted.</summary>
    /// <remarks>The inverse control: a validator mutated to refuse everything fails this one.</remarks>
    [Fact]
    public void A_daily_schedule_within_the_customer_limits_is_accepted()
    {
        Assert.True(_validator.Validate(Command()).IsValid);
    }

    /// <summary>A weekly schedule naming its weekday and keeping the ceiling is accepted.</summary>
    [Fact]
    public void A_weekly_schedule_keeping_the_ceiling_is_accepted()
    {
        var command = Command() with
        {
            Frequency = BackupFrequency.Weekly,
            DayOfWeekUtc = DayOfWeek.Sunday,
            RetainCount = CustomerBackupLimits.MaximumRetainCount,
        };

        Assert.True(_validator.Validate(command).IsValid);
    }

    /// <summary>More copies than the customer ceiling is refused with the customer's own key.</summary>
    /// <remarks>
    /// The number the operator may set (the entity ceiling) is exactly what a customer must not: the
    /// refusal sits one above the ceiling.
    /// </remarks>
    [Fact]
    public void More_copies_than_the_customer_ceiling_is_refused()
    {
        var result = _validator.Validate(Command() with { RetainCount = CustomerBackupLimits.MaximumRetainCount + 1 });

        Assert.False(result.IsValid);
        Assert.Equal("BackupScheduleCustomerRetainTooHigh", Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>Keeping no copy is refused: a schedule that deletes its own result is not a schedule.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Keeping_fewer_than_one_copy_is_refused(int retainCount)
    {
        var result = _validator.Validate(Command() with { RetainCount = retainCount });

        Assert.False(result.IsValid);
        Assert.Equal("BackupScheduleCustomerRetainTooLow", Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>A cadence outside the customer allow-list is refused.</summary>
    [Fact]
    public void A_frequency_off_the_allow_list_is_refused()
    {
        var result = _validator.Validate(Command() with { Frequency = (BackupFrequency)99 });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => { return error.ErrorMessage == "BackupScheduleCustomerFrequencyNotAllowed"; });
    }

    /// <summary>An hour outside the day is refused with the shared key.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void An_hour_outside_the_day_is_refused(int hourUtc)
    {
        var result = _validator.Validate(Command() with { HourUtc = hourUtc });

        Assert.False(result.IsValid);
        Assert.Equal("BackupScheduleHourOutOfRange", Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>A weekly schedule with no weekday is refused.</summary>
    [Fact]
    public void A_weekly_schedule_without_a_weekday_is_refused()
    {
        var result = _validator.Validate(Command() with { Frequency = BackupFrequency.Weekly });

        Assert.False(result.IsValid);
        Assert.Equal("BackupScheduleDayRequired", Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>A daily schedule naming a weekday is refused.</summary>
    [Fact]
    public void A_daily_schedule_naming_a_weekday_is_refused()
    {
        var result = _validator.Validate(Command() with { DayOfWeekUtc = DayOfWeek.Monday });

        Assert.False(result.IsValid);
        Assert.Equal("BackupScheduleDayNotAllowed", Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>Builds a valid daily command.</summary>
    /// <returns>A daily 03:00 schedule keeping five copies.</returns>
    private static SaveMyBackupScheduleCommand Command()
    {
        return new SaveMyBackupScheduleCommand(BackupFrequency.Daily, 3, null, 5, Enabled: true);
    }
}
