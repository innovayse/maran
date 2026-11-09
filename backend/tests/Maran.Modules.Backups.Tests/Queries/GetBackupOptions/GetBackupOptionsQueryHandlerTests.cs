using System.Globalization;
using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Queries.GetBackupOptions;
using Maran.Modules.Backups.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Backups.Tests.Queries.GetBackupOptions;

/// <summary>
/// The closed sets the backup screens select from: what each list holds, that its values are the
/// ones the validators and the stored rows speak, and that every label is a real translation.
/// </summary>
/// <remarks>
/// Run against the module's REAL resource files: a stub localizer would prove only that the handler
/// calls something, whereas the defect worth catching is a key with no entry.
/// </remarks>
public sealed class GetBackupOptionsQueryHandlerTests
{
    /// <summary>The weekday values a schedule stores, Monday first.</summary>
    private static readonly string[] WeekdayValues =
        new[] { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" };

    /// <summary>The run statuses on the wire.</summary>
    private static readonly string[] StatusValues = new[] { "running", "completed", "failed" };

    /// <summary>The backup kinds on the wire.</summary>
    private static readonly string[] KindValues = new[] { "manual", "scheduled", "preDeletion", "preRestore" };

    /// <summary>The destination kinds on the wire.</summary>
    private static readonly string[] DestinationKindValues = new[] { "local", "s3" };

    /// <summary>The schedule frequencies on the wire.</summary>
    private static readonly string[] FrequencyValues = new[] { "daily", "weekly" };

    /// <summary>Asks the handler for the lists in the named culture.</summary>
    /// <param name="culture">The culture of the request.</param>
    /// <returns>The lists.</returns>
    private static async Task<BackupOptionsDto> OptionsInAsync(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var factory = new ResourceManagerStringLocalizerFactory(
                new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
                NullLoggerFactory.Instance);
            var handler = new GetBackupOptionsQueryHandler(new StringLocalizer<DisplayNames>(factory));

            var result = await handler.HandleAsync(new GetBackupOptionsQuery(), CancellationToken.None);

            return result.Value;
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>The hours and the retention counts span exactly what the save validator accepts.</summary>
    [Fact]
    public async Task The_hours_and_retention_counts_span_what_the_save_validator_accepts()
    {
        var options = await OptionsInAsync("en");

        Assert.Equal(24, options.Hours.Count);
        Assert.Equal(("0", "00:00"), (options.Hours[0].Value, options.Hours[0].Label));
        Assert.Equal(("23", "23:00"), (options.Hours[^1].Value, options.Hours[^1].Label));
        Assert.Equal(365, options.RetainCounts.Count);
        Assert.Equal(("1", "1"), (options.RetainCounts[0].Value, options.RetainCounts[0].Label));
        Assert.Equal(("365", "365"), (options.RetainCounts[^1].Value, options.RetainCounts[^1].Label));
    }

    /// <summary>The customer's lists are the allow-listed cadences and the counts up to the customer ceiling.</summary>
    /// <remarks>
    /// A subset of the administrator's lists, in the same vocabulary, so a value read from either is
    /// one a stored row and a validator speak.
    /// </remarks>
    [Fact]
    public async Task The_customer_lists_are_the_allow_listed_cadences_and_the_counts_up_to_the_customer_ceiling()
    {
        var options = await OptionsInAsync("en");

        Assert.Equal(FrequencyValues, options.CustomerFrequencies.Select(choice => { return choice.Value; }));
        Assert.Equal(7, options.CustomerRetainCounts.Count);
        Assert.Equal("1", options.CustomerRetainCounts[0].Value);
        Assert.Equal("7", options.CustomerRetainCounts[^1].Value);
        Assert.All(options.CustomerRetainCounts, choice => { Assert.Contains(choice, options.RetainCounts); });
    }

    /// <summary>Weekdays are valued by the camelCase name a schedule stores, Monday first.</summary>
    [Fact]
    public async Task Weekdays_are_valued_by_the_stored_name_with_monday_first()
    {
        var options = await OptionsInAsync("en");

        Assert.Equal(
            WeekdayValues,
            options.Weekdays.Select(choice => { return choice.Value; }).ToArray());
        Assert.Equal("Monday", options.Weekdays[0].Label);
    }

    /// <summary>Every enum-backed list carries the camelCase names the wire already uses.</summary>
    [Fact]
    public async Task The_enum_lists_use_the_camel_case_wire_names()
    {
        var options = await OptionsInAsync("en");

        Assert.Equal(
            StatusValues,
            options.Statuses.Select(choice => { return choice.Value; }).ToArray());
        Assert.Equal(
            KindValues,
            options.Kinds.Select(choice => { return choice.Value; }).ToArray());
        Assert.Equal(
            DestinationKindValues,
            options.DestinationKinds.Select(choice => { return choice.Value; }).ToArray());
        Assert.Equal(
            FrequencyValues,
            options.Frequencies.Select(choice => { return choice.Value; }).ToArray());
    }

    /// <summary>
    /// Every named label is a translation in every shipped language: a label equal to its resource
    /// key is the localizer reporting a missing entry, and one equal to the English text in ru or hy
    /// is a missing satellite entry answered by the neutral resource.
    /// </summary>
    /// <param name="culture">The culture under test.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public async Task Every_named_label_is_translated(string culture)
    {
        var options = await OptionsInAsync(culture);
        var labels = NamedLabels(options);

        Assert.Equal(18, labels.Length);
        Assert.DoesNotContain(labels, label => { return label.StartsWith("Backup", StringComparison.Ordinal); });

        if (culture != "en")
        {
            var english = NamedLabels(await OptionsInAsync("en"));

            Assert.Empty(labels.Intersect(english, StringComparer.Ordinal));
        }
    }

    /// <summary>Collects the labels that come from resources rather than from arithmetic.</summary>
    /// <param name="options">The lists.</param>
    /// <returns>The status, reason, storage, cadence and weekday labels.</returns>
    private static string[] NamedLabels(BackupOptionsDto options)
    {
        return options.Statuses
            .Concat(options.Kinds)
            .Concat(options.DestinationKinds)
            .Concat(options.Frequencies)
            .Concat(options.Weekdays)
            .Select(choice => { return choice.Label; })
            .ToArray();
    }
}
