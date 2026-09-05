using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Validators;

namespace Maran.Modules.Cron.Tests.Validators;

/// <summary>The cron grammar this panel accepts, field by field, mirroring the agent's own.</summary>
public sealed class CronScheduleValidatorTests
{
    /// <summary>Every ordinary schedule shape is accepted.</summary>
    [Theory]
    [InlineData("0", "3", "*", "*", "*")]
    [InlineData("*", "*", "*", "*", "*")]
    [InlineData("*/5", "*", "*", "*", "*")]
    [InlineData("0,15,30,45", "*", "*", "*", "*")]
    [InlineData("0", "9-17", "*", "*", "1-5")]
    [InlineData("0", "0-23/2", "*", "*", "*")]
    [InlineData("59", "23", "31", "12", "6")]
    [InlineData("0", "0", "1", "1", "0")]
    public void Every_ordinary_schedule_shape_is_accepted(
        string minute, string hour, string dayOfMonth, string month, string dayOfWeek)
    {
        Assert.True(IsValid(new CronScheduleDto(minute, hour, dayOfMonth, month, dayOfWeek)));
    }

    /// <summary>A field outside its own range is refused.</summary>
    [Theory]
    [InlineData("60", "3", "*", "*", "*")]
    [InlineData("0", "24", "*", "*", "*")]
    [InlineData("0", "3", "32", "*", "*")]
    [InlineData("0", "3", "0", "*", "*")]
    [InlineData("0", "3", "*", "13", "*")]
    [InlineData("0", "3", "*", "0", "*")]
    [InlineData("0", "3", "*", "*", "7")]
    public void A_field_outside_its_own_range_is_refused(
        string minute, string hour, string dayOfMonth, string month, string dayOfWeek)
    {
        // The one-based fields are the ones a shared bound would get wrong: day-of-month and month
        // both refuse 0, and day-of-week refuses the 7 that some crons also read as Sunday — two
        // spellings of one day is a value whose meaning depends on which cron the host ships.
        Assert.False(IsValid(new CronScheduleDto(minute, hour, dayOfMonth, month, dayOfWeek)));
    }

    /// <summary>A step larger than the fields own span is refused.</summary>
    [Fact]
    public void A_step_larger_than_the_fields_own_span_is_refused()
    {
        // `*/31` in day-of-month selects the first of the month and nothing else while reading as
        // "every 31 days". The cap is the SPAN between the bounds, not the maximum, and the two
        // differ only in the one-based fields — which is why this test names one of them.
        Assert.False(IsValid(new CronScheduleDto("0", "3", "*/31", "*", "*")));
        Assert.True(IsValid(new CronScheduleDto("0", "3", "*/30", "*", "*")));
    }

    /// <summary>A step after a bare number is refused because a step needs a span.</summary>
    [Fact]
    public void A_step_after_a_bare_number_is_refused_because_a_step_needs_a_span()
    {
        // `5/2` means "every second value from 5" to some crons and is an error to others, so the
        // panel refuses it rather than installing a line whose meaning depends on the host.
        Assert.False(IsValid(new CronScheduleDto("5/2", "*", "*", "*", "*")));
        Assert.True(IsValid(new CronScheduleDto("5-30/2", "*", "*", "*", "*")));
    }

    /// <summary>A zero step is refused.</summary>
    [Fact]
    public void A_zero_step_is_refused()
    {
        Assert.False(IsValid(new CronScheduleDto("*/0", "*", "*", "*", "*")));
    }

    /// <summary>A range that runs backwards is refused.</summary>
    [Fact]
    public void A_range_that_runs_backwards_is_refused()
    {
        Assert.False(IsValid(new CronScheduleDto("30-10", "*", "*", "*", "*")));
    }

    /// <summary>A padded number is refused so that one schedule has one text.</summary>
    [Fact]
    public void A_padded_number_is_refused_so_that_one_schedule_has_one_text()
    {
        // `05` and `5` would be two spellings of one schedule, and a later read would compare them
        // as different — which is how a panel comes to believe a crontab changed when it did not.
        Assert.False(IsValid(new CronScheduleDto("05", "*", "*", "*", "*")));
    }

    /// <summary>Whitespace and control characters are refused anywhere in a field.</summary>
    [Theory]
    [InlineData("0 3")]
    [InlineData("0\t")]
    [InlineData("0\n")]
    [InlineData(" 0")]
    public void Whitespace_and_control_characters_are_refused_anywhere_in_a_field(string minute)
    {
        // The five fields arrive as five values precisely so a space cannot smuggle a sixth, and a
        // newline in a value bound for a crontab line is the injection this refusal exists for.
        Assert.False(IsValid(new CronScheduleDto(minute, "3", "*", "*", "*")));
    }

    /// <summary>Names and shortcuts are refused because the agent renders only what it validated.</summary>
    [Theory]
    [InlineData("@hourly", "*", "*", "*", "*")]
    [InlineData("0", "3", "*", "JAN", "*")]
    [InlineData("0", "3", "*", "*", "MON")]
    [InlineData("+5", "3", "*", "*", "*")]
    public void Names_and_shortcuts_are_refused_because_the_agent_renders_only_what_it_validated(
        string minute, string hour, string dayOfMonth, string month, string dayOfWeek)
    {
        Assert.False(IsValid(new CronScheduleDto(minute, hour, dayOfMonth, month, dayOfWeek)));
    }

    /// <summary>An empty field and an empty item are both refused.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("0,")]
    [InlineData(",0")]
    [InlineData("0,,5")]
    public void An_empty_field_and_an_empty_item_are_both_refused(string minute)
    {
        Assert.False(IsValid(new CronScheduleDto(minute, "3", "*", "*", "*")));
    }

    /// <summary>A field longer than the agents ceiling is refused.</summary>
    [Fact]
    public void A_field_longer_than_the_agents_ceiling_is_refused()
    {
        // Nothing a crontab can express reaches this length — a fully enumerated minute field is 169
        // characters — so what it refuses is a caller padding the line rather than a real schedule.
        var padded = string.Join(',', Enumerable.Repeat("1", 200));

        Assert.False(IsValid(new CronScheduleDto(padded, "3", "*", "*", "*")));
    }

    /// <summary>Runs the validator over one schedule.</summary>
    /// <param name="schedule">The schedule to check.</param>
    /// <returns>Whether the validator accepted it.</returns>
    private static bool IsValid(CronScheduleDto schedule)
    {
        return new CronScheduleValidator().Validate(schedule).IsValid;
    }
}
