using System.Globalization;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Queries.GetCronOptions;
using Maran.Modules.Cron.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Cron.Tests.Queries.GetCronOptions;

/// <summary>
/// The closed sets the scheduled-tasks screens select from: what each list holds, that its values
/// are the ones the validators and the SPA agree on, and that every label is a real translation.
/// </summary>
/// <remarks>
/// Run against the module's REAL resource files: a stub localizer would prove only that the handler
/// calls something, whereas the defect worth catching is a key with no entry, which a localizer
/// answers with the key itself.
/// </remarks>
public sealed class GetCronOptionsQueryHandlerTests
{
    /// <summary>The weekday values, cron's own numbers with Sunday as zero.</summary>
    private static readonly string[] WeekdayNumbers =
        new[] { "0", "1", "2", "3", "4", "5", "6" };

    /// <summary>The weekday labels in English, Sunday first.</summary>
    private static readonly string[] WeekdayNames =
        new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

    /// <summary>The schedule frequencies on the wire.</summary>
    private static readonly string[] FrequencyValues =
        new[] { "everyMinute", "hourly", "daily", "weekly", "monthly" };

    /// <summary>The run outcomes on the wire.</summary>
    private static readonly string[] OutcomeValues =
        new[] { "succeeded", "failed", "stopped" };

    /// <summary>Asks the handler for the lists in the named culture.</summary>
    /// <param name="culture">The culture of the request.</param>
    /// <returns>The lists.</returns>
    private static async Task<CronOptionsDto> OptionsInAsync(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var factory = new ResourceManagerStringLocalizerFactory(
                new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
                NullLoggerFactory.Instance);
            var handler = new GetCronOptionsQueryHandler(new StringLocalizer<DisplayNames>(factory));

            var result = await handler.HandleAsync(new GetCronOptionsQuery(), CancellationToken.None);

            return result.Value;
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>The numeric lists span exactly the ranges the schedule validator accepts.</summary>
    [Fact]
    public async Task The_numeric_lists_span_the_ranges_the_schedule_validator_accepts()
    {
        var options = await OptionsInAsync("en");

        Assert.Equal(24, options.Hours.Count);
        Assert.Equal(("0", "00"), (options.Hours[0].Value, options.Hours[0].Label));
        Assert.Equal(("23", "23"), (options.Hours[^1].Value, options.Hours[^1].Label));
        Assert.Equal(60, options.Minutes.Count);
        Assert.Equal(("59", "59"), (options.Minutes[^1].Value, options.Minutes[^1].Label));
        Assert.Equal(31, options.DaysOfMonth.Count);
        Assert.Equal(("1", "1"), (options.DaysOfMonth[0].Value, options.DaysOfMonth[0].Label));
        Assert.Equal(("31", "31"), (options.DaysOfMonth[^1].Value, options.DaysOfMonth[^1].Label));
    }

    /// <summary>Weekdays are valued by cron's own number, with Sunday as zero.</summary>
    [Fact]
    public async Task Weekdays_are_valued_by_the_number_cron_uses_with_sunday_first()
    {
        var options = await OptionsInAsync("en");

        Assert.Equal(
            WeekdayNumbers,
            options.Weekdays.Select(choice => { return choice.Value; }).ToArray());
        Assert.Equal(
            WeekdayNames,
            options.Weekdays.Select(choice => { return choice.Label; }).ToArray());
    }

    /// <summary>Frequencies and outcomes carry the camelCase names the rest of the panel speaks.</summary>
    [Fact]
    public async Task Frequencies_and_outcomes_use_the_camel_case_wire_names()
    {
        var options = await OptionsInAsync("en");

        Assert.Equal(
            FrequencyValues,
            options.Frequencies.Select(choice => { return choice.Value; }).ToArray());
        Assert.Equal(
            OutcomeValues,
            options.RunOutcomes.Select(choice => { return choice.Value; }).ToArray());
    }

    /// <summary>
    /// Every named label is a translation in every shipped language: a label equal to its resource
    /// key is the localizer reporting a missing entry.
    /// </summary>
    /// <param name="culture">The culture under test.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public async Task Every_named_label_is_translated(string culture)
    {
        var options = await OptionsInAsync(culture);

        var labels = options.Frequencies.Concat(options.Weekdays).Concat(options.RunOutcomes)
            .Select(choice => { return choice.Label; })
            .ToArray();

        Assert.Equal(15, labels.Length);
        Assert.DoesNotContain(labels, label => { return label.StartsWith("Cron", StringComparison.Ordinal); });
        Assert.Equal(labels.Length, labels.Distinct(StringComparer.Ordinal).Count());

        // The neutral resource is English, so a missing ru/hy entry is answered with the English
        // text, not with the key: only a comparison against English can see that.
        if (culture != "en")
        {
            var english = await OptionsInAsync("en");
            var englishLabels = english.Frequencies.Concat(english.Weekdays).Concat(english.RunOutcomes)
                .Select(choice => { return choice.Label; })
                .ToArray();

            Assert.Empty(labels.Intersect(englishLabels, StringComparer.Ordinal));
        }
    }

    /// <summary>The Russian list speaks Russian, so a missing satellite entry cannot hide behind English.</summary>
    [Fact]
    public async Task The_russian_lists_speak_russian()
    {
        var options = await OptionsInAsync("ru");

        Assert.Equal("Каждый день", options.Frequencies.Single(choice => { return choice.Value == "daily"; }).Label);
        Assert.Equal("вторник", options.Weekdays.Single(choice => { return choice.Value == "2"; }).Label);
        Assert.Equal("Ошибка", options.RunOutcomes.Single(choice => { return choice.Value == "failed"; }).Label);
    }
}
