using Maran.Modules.Licensing.Models;
using Maran.Modules.Licensing.Services;
using Maran.Modules.Licensing.Tests.TestSupport;
using Maran.Sdk.Alerts;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Wolverine;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Covers <see cref="LicenceCoverageWatcher"/>: a lapse and a recovery are written to the audit journal as
/// the panel, once each, and nothing else is. Everything runs over the REAL verifier, source and cache,
/// with a clock the test moves; the only double is the journal's writer.
/// </summary>
public sealed class LicenceCoverageWatcherTests
{
    /// <summary>A lease issued at the harness start and valid for fourteen days.</summary>
    private static LeaseReading.Present ValidLease()
    {
        return new LeaseReading.Present(
            new LicenceLease("lic-001", CoverageHarness.Start, CoverageHarness.Start.AddDays(14), null));
    }

    /// <summary>A licence naming mail, signed by the trusted key.</summary>
    /// <param name="expiry">When it expires.</param>
    /// <returns>The licence file text.</returns>
    private static string Licence(DateTimeOffset expiry)
    {
        return LicenceEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, modules: ["mail"], expiry: expiry);
    }

    /// <summary>Builds a watcher over the harness, writing its entries into <paramref name="writer"/>.</summary>
    /// <param name="harness">The licence window's real parts.</param>
    /// <param name="writer">Where the journal's entries go.</param>
    /// <returns>The watcher and the provider that owns its scopes.</returns>
    /// <param name="bus">Where the expiry alert's outputs are published; a throwaway one when null.</param>
    /// <param name="recipients">Where operator alerts are addressed; one address when null.</param>
    private static (LicenceCoverageWatcher Watcher, ServiceProvider Provider) Watching(
        CoverageHarness harness,
        IAuditWriter writer,
        RecordingMessageBus? bus = null,
        IAlertRecipientDirectory? recipients = null)
    {
        var provider = new ServiceCollection()
            .AddSingleton(writer)
            .AddSingleton<ICurrentUser>(new FakeCurrentUser(isAdmin: true))
            .AddSingleton<IMessageBus>(bus ?? new RecordingMessageBus())
            .AddSingleton<IClock>(harness.Clock)
            .AddSingleton(recipients ?? StubAlertRecipientDirectory.Of([RenewalWorld.OperatorAddress]))
            .AddSingleton(RenewalWorld.AlertText())
            .AddScoped<OperatorAlertDispatcher>()
            .AddScoped<LicensingAuditJournal>()
            .AddScoped<LicenceExpiryAlerter>()
            .BuildServiceProvider();
        var watcher = new LicenceCoverageWatcher(
            harness.Cache,
            LicensingTestContext.ExpiryWarnings(harness.Clock),
            harness.Announcements,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<LicenceCoverageWatcher>.Instance);

        return (watcher, provider);
    }

    /// <summary>A covered module is not journalled, however often it is watched.</summary>
    [Fact]
    public async Task A_covered_module_is_not_journalled()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(ValidLease()));
        harness.Install(Licence(CoverageHarness.Start.AddDays(90)));
        var writer = new RecordingAuditWriter();
        var (watcher, provider) = Watching(harness, writer);
        await using var _ = provider;

        await watcher.ObserveAsync(CancellationToken.None);
        await watcher.ObserveAsync(CancellationToken.None);

        Assert.Empty(writer.Entries);
    }

    /// <summary>A module whose cover runs out is journalled once, as the panel, with the module and the two verdicts.</summary>
    [Fact]
    public async Task A_module_whose_cover_runs_out_is_journalled_once_as_the_panel()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(ValidLease()));
        harness.Install(Licence(CoverageHarness.Start.AddDays(90)));
        var writer = new RecordingAuditWriter();
        var (watcher, provider) = Watching(harness, writer);
        await using var _ = provider;
        await watcher.ObserveAsync(CancellationToken.None);

        harness.Clock.Advance(TimeSpan.FromDays(15));
        await watcher.ObserveAsync(CancellationToken.None);
        await watcher.ObserveAsync(CancellationToken.None);

        var entry = Assert.Single(writer.Entries);
        Assert.Equal(LicenceAuditActions.ModuleLapsed, entry.Action);
        Assert.Equal("module=mail;from=Covered;to=Lapsed", entry.Subject);
        Assert.Equal(SystemAuditEntry.NameFor("licensing"), entry.ActorUsername);
        Assert.Null(entry.ActorUserId);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A lapsed module that is covered again by a new licence is journalled as recovered.</summary>
    [Fact]
    public async Task A_lapsed_module_that_is_covered_again_is_journalled_as_recovered()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(ValidLease()));
        harness.Install(Licence(CoverageHarness.Start.AddDays(5)));
        var writer = new RecordingAuditWriter();
        var (watcher, provider) = Watching(harness, writer);
        await using var _ = provider;
        await watcher.ObserveAsync(CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromDays(6));
        await watcher.ObserveAsync(CancellationToken.None);

        harness.Install(Licence(CoverageHarness.Start.AddDays(90)));
        harness.Cache.Invalidate();
        await watcher.ObserveAsync(CancellationToken.None);

        // The expiry alert a licence this close to its end raises is journalled beside the transitions; this test is about those.
        var transitions = writer.Entries.Where(entry => { return entry.Action != MonitoringAuditActions.AlertRaised; }).ToList();
        Assert.Equal(
            [LicenceAuditActions.ModuleLapsed, LicenceAuditActions.ModuleRecovered],
            transitions.Select(entry => { return entry.Action; }));
        Assert.Equal("module=mail;from=Lapsed;to=Covered", transitions[1].Subject);
    }

    /// <summary>A module found lapsed on the first reading is journalled, saying it was not observed before.</summary>
    [Fact]
    public async Task A_module_found_lapsed_on_the_first_reading_is_journalled_as_unobserved_before()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance));
        harness.Install(Licence(CoverageHarness.Start.AddDays(90)));
        var writer = new RecordingAuditWriter();
        var (watcher, provider) = Watching(harness, writer);
        await using var _ = provider;

        await watcher.ObserveAsync(CancellationToken.None);

        var entry = Assert.Single(writer.Entries);
        Assert.Equal("module=mail;from=Unobserved;to=Lapsed", entry.Subject);
    }

    /// <summary>With no licence installed nothing is named, nothing is asked and nothing is journalled.</summary>
    [Fact]
    public async Task With_no_licence_nothing_is_journalled()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(ValidLease()));
        var writer = new RecordingAuditWriter();
        var (watcher, provider) = Watching(harness, writer);
        await using var _ = provider;

        await watcher.ObserveAsync(CancellationToken.None);

        Assert.Empty(writer.Entries);
    }

    /// <summary>A journal that cannot write is retried at the next pass, and the lapse is journalled once it can.</summary>
    [Fact]
    public async Task A_lapse_the_journal_could_not_write_is_journalled_at_the_next_pass()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(ValidLease()));
        harness.Install(Licence(CoverageHarness.Start.AddDays(5)));
        var writer = new FailingOnceAuditWriter();
        var (watcher, provider) = Watching(harness, writer);
        await using var _ = provider;
        await watcher.ObserveAsync(CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromDays(6));

        await watcher.ObserveAsync(CancellationToken.None);
        Assert.DoesNotContain(writer.Entries, entry => { return entry.Action == LicenceAuditActions.ModuleLapsed; });

        await watcher.ObserveAsync(CancellationToken.None);
        Assert.Single(writer.Entries, entry => { return entry.Action == LicenceAuditActions.ModuleLapsed; });
    }

    /// <summary>An audit writer whose first write of a lapse fails, so the retry is observable.</summary>
    private sealed class FailingOnceAuditWriter : IAuditWriter
    {
        /// <summary>Whether the failure has been spent. Only a lapse fails: the expiry alert a licence this close to its end raises is written beside it.</summary>
        private bool _failed;

        /// <summary>The entries that were written.</summary>
        public List<AuditEntry> Entries { get; } = [];

        /// <inheritdoc />
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
        {
            if (!_failed && entry.Action == LicenceAuditActions.ModuleLapsed)
            {
                _failed = true;
                throw new InvalidOperationException("planted journal failure");
            }

            Entries.Add(entry);

            return Task.CompletedTask;
        }
    }

    /// <summary>A licence further than the widest threshold from its expiry raises nothing, however often it is watched.</summary>
    [Fact]
    public async Task A_licence_outside_every_threshold_raises_no_expiry_alert()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(ValidLease()));
        harness.Install(Licence(CoverageHarness.Start.AddDays(30).AddSeconds(1)));
        var bus = new RecordingMessageBus();
        var (watcher, provider) = Watching(harness, new RecordingAuditWriter(), bus);
        await using var _ = provider;

        await watcher.ObserveAsync(CancellationToken.None);
        await watcher.ObserveAsync(CancellationToken.None);

        Assert.Empty(bus.Published);
    }

    /// <summary>
    /// Each threshold crossed raises one alert, however many passes see it: 30 days, then 14, then 3. The alert is a
    /// licensing event with the threshold and the days left, mailed to the operator and published to the feed.
    /// </summary>
    [Fact]
    public async Task Each_threshold_crossed_raises_exactly_one_alert()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(ValidLease()));
        harness.Install(Licence(CoverageHarness.Start.AddDays(40)));
        var bus = new RecordingMessageBus();
        var (watcher, provider) = Watching(harness, new RecordingAuditWriter(), bus);
        await using var _ = provider;

        await watcher.ObserveAsync(CancellationToken.None);
        Assert.Empty(bus.Published);

        harness.Clock.Advance(TimeSpan.FromDays(10));
        await watcher.ObserveAsync(CancellationToken.None);
        await watcher.ObserveAsync(CancellationToken.None);
        Assert.Equal(["thresholdDays=30 daysLeft=30 expiry=2026-02-10"], Alerts(bus).Select(Detail));

        harness.Clock.Advance(TimeSpan.FromDays(16));
        harness.Cache.Invalidate();
        await watcher.ObserveAsync(CancellationToken.None);
        await watcher.ObserveAsync(CancellationToken.None);

        harness.Clock.Advance(TimeSpan.FromDays(11));
        harness.Cache.Invalidate();
        await watcher.ObserveAsync(CancellationToken.None);
        await watcher.ObserveAsync(CancellationToken.None);

        Assert.Equal(
            [
                "thresholdDays=30 daysLeft=30 expiry=2026-02-10",
                "thresholdDays=14 daysLeft=14 expiry=2026-02-10",
                "thresholdDays=3 daysLeft=3 expiry=2026-02-10",
            ],
            Alerts(bus).Select(Detail));
        Assert.All(Alerts(bus), alert =>
        {
            Assert.Equal(AlertSource.Licensing, alert.Source);
            Assert.Equal(AlertTransition.Occurred, alert.Transition);
        });
        Assert.Equal(3, bus.Published.OfType<SendMailRequested>().Count());
        Assert.Equal(3, bus.Published.OfType<PanelNotificationRaised>().Count());
    }

    /// <summary>Installing a licence further out clears the standing warning, so the next approach to a threshold is announced again.</summary>
    [Fact]
    public async Task Installing_a_new_licence_clears_the_warning_and_the_next_crossing_is_announced()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(ValidLease()));
        harness.Install(Licence(CoverageHarness.Start.AddDays(20)));
        var bus = new RecordingMessageBus();
        var (watcher, provider) = Watching(harness, new RecordingAuditWriter(), bus);
        await using var _ = provider;
        await watcher.ObserveAsync(CancellationToken.None);
        Assert.Single(Alerts(bus));

        harness.Install(Licence(CoverageHarness.Start.AddDays(365)));
        harness.Cache.Invalidate();
        await watcher.ObserveAsync(CancellationToken.None);
        Assert.Single(Alerts(bus));

        harness.Install(Licence(CoverageHarness.Start.AddDays(25)));
        harness.Cache.Invalidate();
        await watcher.ObserveAsync(CancellationToken.None);

        Assert.Equal(2, Alerts(bus).Count);
    }

    /// <summary>An expired licence and a panel with no licence raise no expiry alert; the approaching one above is the control.</summary>
    [Fact]
    public async Task An_expired_licence_and_no_licence_raise_no_expiry_alert()
    {
        using var expired = new CoverageHarness(new CountingLeaseSource(ValidLease()));
        expired.Install(Licence(CoverageHarness.Start.AddDays(2)));
        expired.Clock.Advance(TimeSpan.FromDays(3));
        using var none = new CoverageHarness(new CountingLeaseSource(LeaseReading.Absent.Instance));
        var expiredBus = new RecordingMessageBus();
        var noneBus = new RecordingMessageBus();
        var (expiredWatcher, expiredProvider) = Watching(expired, new RecordingAuditWriter(), expiredBus);
        var (noneWatcher, noneProvider) = Watching(none, new RecordingAuditWriter(), noneBus);
        await using var _ = expiredProvider;
        await using var __ = noneProvider;

        await expiredWatcher.ObserveAsync(CancellationToken.None);
        await noneWatcher.ObserveAsync(CancellationToken.None);

        Assert.Empty(Alerts(expiredBus));
        Assert.Empty(Alerts(noneBus));
    }

    /// <summary>
    /// A panel that restarts inside a threshold does not announce it again, because what was announced is stored: a
    /// second watcher over the same note stays silent. The control is a watcher over an empty note (a first start),
    /// which announces.
    /// </summary>
    [Fact]
    public async Task A_restarted_panel_does_not_announce_the_same_threshold_again()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(ValidLease()));
        harness.Install(Licence(CoverageHarness.Start.AddDays(20)));
        var bus = new RecordingMessageBus();
        var (first, firstProvider) = Watching(harness, new RecordingAuditWriter(), bus);
        await using var _ = firstProvider;
        await first.ObserveAsync(CancellationToken.None);
        Assert.Single(Alerts(bus));

        var (restarted, restartedProvider) = Watching(harness, new RecordingAuditWriter(), bus);
        await using var __ = restartedProvider;
        await restarted.ObserveAsync(CancellationToken.None);
        await restarted.ObserveAsync(CancellationToken.None);
        Assert.Single(Alerts(bus));

        await harness.Announcements.ForgetAsync(CancellationToken.None);
        var (fresh, freshProvider) = Watching(harness, new RecordingAuditWriter(), bus);
        await using var ___ = freshProvider;
        await fresh.ObserveAsync(CancellationToken.None);
        Assert.Equal(2, Alerts(bus).Count);
    }

    /// <summary>The restarted panel still announces the NEXT threshold: the stored note names 14, and at 3 days it is news.</summary>
    [Fact]
    public async Task A_restarted_panel_announces_the_next_threshold()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(ValidLease()));
        harness.Install(Licence(CoverageHarness.Start.AddDays(20)));
        var bus = new RecordingMessageBus();
        var (first, firstProvider) = Watching(harness, new RecordingAuditWriter(), bus);
        await using var _ = firstProvider;
        await first.ObserveAsync(CancellationToken.None);

        harness.Clock.Advance(TimeSpan.FromDays(17));
        harness.Cache.Invalidate();
        var (restarted, restartedProvider) = Watching(harness, new RecordingAuditWriter(), bus);
        await using var __ = restartedProvider;
        await restarted.ObserveAsync(CancellationToken.None);

        Assert.Equal(2, Alerts(bus).Count);
        Assert.Contains("thresholdDays=3 ", Alerts(bus)[1].Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// A pass that cannot read the licence (the file is being replaced, or is unreadable) does not reset the memory:
    /// the next good pass at the same threshold is silent. The control is a pass that READS a licence further out,
    /// which does reset it, so the approach is announced again.
    /// </summary>
    [Fact]
    public async Task A_pass_that_cannot_read_the_licence_does_not_forget_what_was_announced()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(ValidLease()));
        var near = Licence(CoverageHarness.Start.AddDays(10));
        harness.Install(near);
        var bus = new RecordingMessageBus();
        var (watcher, provider) = Watching(harness, new RecordingAuditWriter(), bus);
        await using var _ = provider;
        await watcher.ObserveAsync(CancellationToken.None);
        Assert.Single(Alerts(bus));

        File.Delete(harness.LicenceFilePath);
        harness.Cache.Invalidate();
        await watcher.ObserveAsync(CancellationToken.None);
        harness.Install("not a licence");
        harness.Cache.Invalidate();
        await watcher.ObserveAsync(CancellationToken.None);
        harness.Install(near);
        harness.Cache.Invalidate();
        await watcher.ObserveAsync(CancellationToken.None);
        Assert.Single(Alerts(bus));

        harness.Install(Licence(CoverageHarness.Start.AddDays(365)));
        harness.Cache.Invalidate();
        await watcher.ObserveAsync(CancellationToken.None);
        harness.Install(near);
        harness.Cache.Invalidate();
        await watcher.ObserveAsync(CancellationToken.None);
        Assert.Equal(2, Alerts(bus).Count);
    }

    /// <summary>A replacement licence with another expiry inside the same threshold is a different fact and is announced.</summary>
    [Fact]
    public async Task A_replacement_licence_with_another_expiry_is_announced()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(ValidLease()));
        harness.Install(Licence(CoverageHarness.Start.AddDays(10)));
        var bus = new RecordingMessageBus();
        var (watcher, provider) = Watching(harness, new RecordingAuditWriter(), bus);
        await using var _ = provider;
        await watcher.ObserveAsync(CancellationToken.None);

        harness.Install(Licence(CoverageHarness.Start.AddDays(12)));
        harness.Cache.Invalidate();
        await watcher.ObserveAsync(CancellationToken.None);

        Assert.Equal(2, Alerts(bus).Count);
    }

    /// <summary>
    /// An alert that could not find where to go publishes and journals nothing, and is not remembered as told, so the
    /// next pass sends it ONCE; the control is the pass after that, which stays silent.
    /// </summary>
    [Fact]
    public async Task An_alert_whose_recipients_could_not_be_read_repeats_nothing_when_it_is_retried()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(ValidLease()));
        harness.Install(Licence(CoverageHarness.Start.AddDays(10)));
        var bus = new RecordingMessageBus();
        var writer = new RecordingAuditWriter();
        var (watcher, provider) = Watching(
            harness, writer, bus, new FailingAlertRecipientDirectory(2, RenewalWorld.OperatorAddress));
        await using var _ = provider;

        await watcher.ObserveAsync(CancellationToken.None);
        await watcher.ObserveAsync(CancellationToken.None);
        Assert.Empty(bus.Published);
        Assert.Empty(writer.Entries);
        Assert.Null(await harness.Announcements.ReadAsync(CancellationToken.None));

        await watcher.ObserveAsync(CancellationToken.None);
        await watcher.ObserveAsync(CancellationToken.None);

        Assert.Single(Alerts(bus));
        Assert.Single(bus.Published.OfType<PanelNotificationRaised>());
        Assert.Single(writer.Entries, entry => { return entry.Action == MonitoringAuditActions.AlertRaised; });
        Assert.Equal(
            new LicenceExpiryAnnouncement(CoverageHarness.Start.AddDays(10), 14),
            await harness.Announcements.ReadAsync(CancellationToken.None));
    }

    /// <summary>
    /// An alert that fails AFTER its journal row was written is remembered as told, so the next pass does not write the
    /// row again; the inverse control is the run with no failure, which writes the one row and the mail's skip entry.
    /// </summary>
    /// <remarks>
    /// With the note stored only after the whole alert returned, the failing run would journal the row on every pass
    /// and this test would see three of them.
    /// </remarks>
    [Fact]
    public async Task An_alert_that_fails_after_its_journal_row_is_not_written_again_on_the_next_pass()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(ValidLease()));
        harness.Install(Licence(CoverageHarness.Start.AddDays(10)));
        var bus = new RecordingMessageBus();
        var writer = new RecordingAuditWriter { FailAfterWrites = 1 };
        var (watcher, provider) = Watching(harness, writer, bus, StubAlertRecipientDirectory.Of([]));
        await using var _ = provider;

        await watcher.ObserveAsync(CancellationToken.None);
        await watcher.ObserveAsync(CancellationToken.None);
        await watcher.ObserveAsync(CancellationToken.None);

        Assert.Single(writer.Entries, entry => { return entry.Action == MonitoringAuditActions.AlertRaised; });
        Assert.Single(Alerts(bus));
        Assert.Equal(
            new LicenceExpiryAnnouncement(CoverageHarness.Start.AddDays(10), 14),
            await harness.Announcements.ReadAsync(CancellationToken.None));
    }

    /// <summary>With a working journal and no recipient the same alert writes its row and the skip entry, once.</summary>
    [Fact]
    public async Task An_alert_with_no_recipient_writes_its_row_and_the_skip_entry_once()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(ValidLease()));
        harness.Install(Licence(CoverageHarness.Start.AddDays(10)));
        var writer = new RecordingAuditWriter();
        var (watcher, provider) = Watching(harness, writer, new RecordingMessageBus(), StubAlertRecipientDirectory.Of([]));
        await using var _ = provider;

        await watcher.ObserveAsync(CancellationToken.None);
        await watcher.ObserveAsync(CancellationToken.None);

        Assert.Equal(2, writer.Entries.Count);
        Assert.Single(writer.Entries, entry => { return entry.Action == MonitoringAuditActions.AlertRaised; });
    }

    /// <summary>The feed entry leads to the licence page and carries the day and the days left for its sentence.</summary>
    [Fact]
    public async Task The_feed_entry_leads_to_the_licence_page_and_carries_the_day_and_days_left()
    {
        using var harness = new CoverageHarness(new CountingLeaseSource(ValidLease()));
        harness.Install(Licence(CoverageHarness.Start.AddDays(10)));
        var bus = new RecordingMessageBus();
        var (watcher, provider) = Watching(harness, new RecordingAuditWriter(), bus);
        await using var _ = provider;

        await watcher.ObserveAsync(CancellationToken.None);

        var entry = Assert.Single(bus.Published.OfType<PanelNotificationRaised>());
        Assert.Equal(NotificationDeepLinks.Licence, entry.DeepLink);
        Assert.Equal(["2026-01-11", "10"], entry.Arguments);
    }

    /// <summary>The alerts in a bus's record.</summary>
    /// <param name="bus">The bus.</param>
    /// <returns>The operator alerts published, in order.</returns>
    private static List<OperatorAlert> Alerts(RecordingMessageBus bus)
    {
        return bus.Published.OfType<OperatorAlertRaised>().Select(raised => { return raised.Alert; }).ToList();
    }

    /// <summary>The detail of an alert.</summary>
    /// <param name="alert">The alert.</param>
    /// <returns>Its detail.</returns>
    private static string Detail(OperatorAlert alert)
    {
        return alert.Detail;
    }
}
