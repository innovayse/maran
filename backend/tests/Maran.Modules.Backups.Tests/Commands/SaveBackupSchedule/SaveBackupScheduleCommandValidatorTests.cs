using Maran.Modules.Backups.Commands.SaveBackupSchedule;
using Maran.Modules.Backups.Domain.Enums;

namespace Maran.Modules.Backups.Tests.Commands.SaveBackupSchedule;

/// <summary>What the boundary accepts as a schedule, and what it refuses before a handler sees it.</summary>
public sealed class SaveBackupScheduleCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly SaveBackupScheduleCommandValidator _validator = new();

    /// <summary>A daily schedule at a real hour with a real retention is accepted.</summary>
    /// <remarks>
    /// The inverse control: a validator mutated to refuse everything passes every refusal below and
    /// fails this one.
    /// </remarks>
    [Fact]
    public void A_daily_schedule_at_a_real_hour_is_accepted()
    {
        Assert.True(_validator.Validate(Command()).IsValid);
    }

    /// <summary>A weekly schedule naming its weekday is accepted.</summary>
    [Fact]
    public void A_weekly_schedule_naming_its_weekday_is_accepted()
    {
        var command = Command() with { Frequency = BackupFrequency.Weekly, DayOfWeekUtc = DayOfWeek.Sunday };

        Assert.True(_validator.Validate(command).IsValid);
    }

    /// <summary>An hour outside the day is refused with a bare resource key.</summary>
    /// <remarks>
    /// The message is asserted as well as the refusal, because <c>ExceptionMiddleware</c> forwards a
    /// validation message only when it is entirely alphanumeric — an English sentence here would be
    /// discarded and the caller told nothing but "invalid request".
    /// </remarks>
    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void An_hour_outside_the_day_is_refused(int hourUtc)
    {
        var result = _validator.Validate(Command() with { HourUtc = hourUtc });

        Assert.False(result.IsValid);
        Assert.Equal("BackupScheduleHourOutOfRange", Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>A retention outside the allowed range is refused.</summary>
    /// <remarks>
    /// Zero would mean a schedule whose retention deletes the backup its own run has just taken; the
    /// upper bound is what stops a number typed into a form filling a disk months later.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public void A_retention_outside_the_allowed_range_is_refused(int retainCount)
    {
        var result = _validator.Validate(Command() with { RetainCount = retainCount });

        Assert.False(result.IsValid);
        Assert.Equal("BackupScheduleRetainOutOfRange", Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>A weekly schedule with no weekday is refused.</summary>
    [Fact]
    public void A_weekly_schedule_with_no_weekday_is_refused()
    {
        var result = _validator.Validate(Command() with { Frequency = BackupFrequency.Weekly });

        Assert.False(result.IsValid);
        Assert.Equal("BackupScheduleDayRequired", Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>A daily schedule carrying a weekday is refused.</summary>
    /// <remarks>
    /// The other direction, and not pedantry: the sweep ignores the weekday of a daily schedule, so
    /// a form that accepted one would show an operator a day the server does not honour.
    /// </remarks>
    [Fact]
    public void A_daily_schedule_carrying_a_weekday_is_refused()
    {
        var result = _validator.Validate(Command() with { DayOfWeekUtc = DayOfWeek.Sunday });

        Assert.False(result.IsValid);
        Assert.Equal("BackupScheduleDayNotAllowed", Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>An empty account id is refused, while an absent one is the host-wide policy.</summary>
    [Fact]
    public void An_empty_account_id_is_refused_and_an_absent_one_is_the_host_policy()
    {
        Assert.False(_validator.Validate(Command() with { AccountId = Guid.Empty }).IsValid);
        Assert.True(_validator.Validate(Command() with { AccountId = null }).IsValid);
    }

    /// <summary>Builds a valid command to mutate one field of.</summary>
    /// <returns>An enabled daily schedule at 03:00 keeping seven backups.</returns>
    private static SaveBackupScheduleCommand Command()
    {
        return new SaveBackupScheduleCommand(null, null, BackupFrequency.Daily, 3, null, 7, Enabled: true);
    }
}
