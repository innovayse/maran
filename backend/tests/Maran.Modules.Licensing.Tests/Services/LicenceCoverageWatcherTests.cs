using Maran.Modules.Licensing.Services;
using Maran.Modules.Licensing.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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
    private static (LicenceCoverageWatcher Watcher, ServiceProvider Provider) Watching(
        CoverageHarness harness,
        IAuditWriter writer)
    {
        var provider = new ServiceCollection()
            .AddSingleton(writer)
            .AddSingleton<ICurrentUser>(new FakeCurrentUser(isAdmin: true))
            .AddScoped<LicensingAuditJournal>()
            .BuildServiceProvider();
        var watcher = new LicenceCoverageWatcher(
            harness.Cache,
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

        Assert.Equal(
            [LicenceAuditActions.ModuleLapsed, LicenceAuditActions.ModuleRecovered],
            writer.Entries.Select(entry => { return entry.Action; }));
        Assert.Equal("module=mail;from=Lapsed;to=Covered", writer.Entries[1].Subject);
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
        Assert.Empty(writer.Entries);

        await watcher.ObserveAsync(CancellationToken.None);
        Assert.Single(writer.Entries);
    }

    /// <summary>An audit writer whose first write fails, so the retry is observable.</summary>
    private sealed class FailingOnceAuditWriter : IAuditWriter
    {
        /// <summary>Whether the failure has been spent.</summary>
        private bool _failed;

        /// <summary>The entries that were written.</summary>
        public List<AuditEntry> Entries { get; } = [];

        /// <inheritdoc />
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
        {
            if (!_failed)
            {
                _failed = true;
                throw new InvalidOperationException("planted journal failure");
            }

            Entries.Add(entry);

            return Task.CompletedTask;
        }
    }
}
