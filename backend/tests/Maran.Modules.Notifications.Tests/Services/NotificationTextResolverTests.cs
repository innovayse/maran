using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Domain.ValueObjects;
using Maran.Modules.Notifications.Resources;
using Maran.Modules.Notifications.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Notifications.Tests.Services;

/// <summary>How a stored entry becomes words: which key it asks for, and what it falls back to.</summary>
public sealed class NotificationTextResolverTests
{
    /// <summary>A task failure's headline is the task key and its detail is the failure code's sentence.</summary>
    [Fact]
    public void A_task_failure_reads_the_code_s_sentence_as_its_detail()
    {
        var entry = Entry(NotificationSource.Task, "SiteNotFound", NotificationSeverity.Warning, "example.com");

        var (title, detail) = FeedWorld.Resolver(new() { ["SiteNotFound"] = "That site does not exist." }).Resolve(entry);

        Assert.StartsWith("FeedTaskFailed|example.com", title, StringComparison.Ordinal);
        Assert.Equal("That site does not exist.", detail);
    }

    /// <summary>A code no table claims reads as the generic sentence, never as the code.</summary>
    [Fact]
    public void An_unclaimed_task_code_reads_as_the_generic_sentence()
    {
        var entry = Entry(NotificationSource.Task, "NoSuchCode", NotificationSeverity.Warning, "x");

        var (_, detail) = FeedWorld.Resolver().Resolve(entry);

        Assert.Equal("FeedTaskFailureUnexplained", detail);
    }

    /// <summary>An alert asks for the key made of source, code and severity, with the subject as the first argument.</summary>
    [Fact]
    public void An_alert_asks_for_the_key_made_of_source_code_and_severity()
    {
        var entry = Entry(NotificationSource.Alert, "DiskUsage", NotificationSeverity.Critical, "/var", "91%");

        var (title, detail) = FeedWorld.Resolver().Resolve(entry);

        Assert.StartsWith("FeedAlertDiskUsageCritical|/var|91%", title, StringComparison.Ordinal);
        Assert.Equal("91%", detail);
    }

    /// <summary>The shipped resx has an entry for every alert and backup key the dispatcher can ask for, in all three languages.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="culture">The culture to read it in.</param>
    [Theory]
    [InlineData("FeedAlertDiskUsageCritical", "en")]
    [InlineData("FeedAlertDiskUsageInfo", "ru")]
    [InlineData("FeedAlertCpuUsageCritical", "hy")]
    [InlineData("FeedAlertMemoryUsageInfo", "en")]
    [InlineData("FeedBackupScheduledRunFailedWarning", "ru")]
    [InlineData("FeedAlertPanelUpdateRolledBackCritical", "hy")]
    [InlineData("FeedAlertPanelUpdateRollbackFailedCritical", "ru")]
    [InlineData("FeedTaskFailed", "hy")]
    [InlineData("FeedAlertGeneric", "en")]
    [InlineData("FeedAlertLicenceExpiringWarning", "ru")]
    [InlineData("FeedAlertLicenceExpiringWarning", "hy")]
    public void The_shipped_resources_name_the_key_in_each_language(string key, string culture)
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
            NullLoggerFactory.Instance);
        var localizer = new StringLocalizer<NotificationMessages>(factory);
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("en");
            var english = localizer[key];

            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo(culture);
            var localized = localizer[key];

            Assert.False(localized.ResourceNotFound);
            // The neutral table answers for a language that lacks the key, so "found" alone would pass
            // for a missing translation; a ru or hy sentence must differ from the English one.
            if (culture != "en")
            {
                Assert.NotEqual(english.Value, localized.Value);
            }
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>
    /// The licence expiry sentence names the expiry day and the days left in every language (the feed values are the
    /// day and the days left). The control is the arguments being absent, which leaves neither figure in the sentence.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public void The_licence_expiry_sentence_names_the_day_and_the_days_left(string culture)
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
            NullLoggerFactory.Instance);
        var localizer = new StringLocalizer<NotificationMessages>(factory);
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo(culture);

            var with = localizer["FeedAlertLicenceExpiringWarning", "licence", "2026-12-26", "14", string.Empty, string.Empty].Value;
            var without = localizer["FeedAlertLicenceExpiringWarning", "licence", string.Empty, string.Empty, string.Empty, string.Empty].Value;

            Assert.Contains("2026-12-26", with, StringComparison.Ordinal);
            Assert.Contains("14", with, StringComparison.Ordinal);
            Assert.DoesNotContain("2026-12-26", without, StringComparison.Ordinal);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }

    private static PanelNotification Entry(
        NotificationSource source,
        string code,
        NotificationSeverity severity,
        string subject,
        params string[] arguments)
    {
        return new PanelNotification(
            Guid.NewGuid(), source, code, severity, subject, PanelNotificationArguments.From(arguments), null, FeedWorld.Now);
    }
}
