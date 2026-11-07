using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Jobs;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Identity.Tests.Jobs;

/// <summary>
/// Behavioural contract of the audit maintenance tick: it seals on every tick, and once a day it applies
/// retention, sweeps orphaned actors and verifies the chain, journalling each run.
/// </summary>
public sealed class AuditMaintenanceHandlerTests : IDisposable
{
    /// <summary>The instant every test treats as the present.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The in-memory context every service in the tick shares.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    /// <summary>Keeps the entries the journal writes.</summary>
    private readonly RecordingAuditWriter _audit = new();
    /// <summary>The clock the handler measures the daily gate against.</summary>
    private readonly FakeClock _clock = new(Now);

    /// <summary>Releases the in-memory context this test owns.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Builds an unsealed journal row that happened the given number of days before now.</summary>
    /// <param name="days">How many days ago the row happened.</param>
    /// <param name="subject">The row's subject.</param>
    /// <returns>The unsealed row.</returns>
    private static AuditEvent RowAged(double days, string subject)
    {
        return AuditChainFixture.Row(Now.AddDays(-days), subject: subject);
    }

    /// <summary>Builds the handler over real services sharing this test's context.</summary>
    /// <param name="retentionClock">A clock for the retention runner alone, to make that step fail.</param>
    /// <returns>The handler under test.</returns>
    private AuditMaintenanceHandler NewHandler(IClock? retentionClock = null)
    {
        var journal = new IdentityAuditJournal(_audit, new StubCurrentUser());

        return new AuditMaintenanceHandler(
            _context,
            new AuditChainSealer(_context, AuditChainFixture.Deriver()),
            new AuditRetentionRunner(_context, retentionClock ?? _clock, NullLogger<AuditRetentionRunner>.Instance),
            new AuditPseudonymiser(_context, _clock, journal, NullLogger<AuditPseudonymiser>.Instance),
            AuditChainFixture.Verifier(_context),
            journal,
            _clock,
            NullLogger<AuditMaintenanceHandler>.Instance);
    }

    /// <summary>Runs one maintenance tick.</summary>
    /// <param name="retentionClock">A clock for the retention runner alone, to make that step fail.</param>
    /// <returns>Resolves once the tick has finished.</returns>
    private Task TickAsync(IClock? retentionClock = null)
    {
        return NewHandler(retentionClock).HandleAsync(new AuditMaintenanceRequested(), CancellationToken.None);
    }

    /// <summary>Stores unsealed rows.</summary>
    /// <param name="rows">The rows to store.</param>
    /// <returns>Resolves once they are stored.</returns>
    private async Task AddAsync(params AuditEvent[] rows)
    {
        _context.AuditEvents.AddRange(rows);
        await _context.SaveChangesAsync();
    }

    /// <summary>Seals every stored row with the real sealer.</summary>
    /// <returns>Resolves once they are sealed.</returns>
    private async Task SealNowAsync()
    {
        await new AuditChainSealer(_context, AuditChainFixture.Deriver())
            .SealAsync(AuditChainSealer.MaxBatchesPerRun, CancellationToken.None);
    }

    /// <summary>Stores the retention policy row.</summary>
    /// <param name="days">The window in days, or null for keep indefinitely.</param>
    /// <param name="lastRunAt">When the daily run last finished, or null for never.</param>
    /// <returns>Resolves once the row is stored.</returns>
    private async Task SetPolicyAsync(int? days, DateTimeOffset? lastRunAt)
    {
        var policy = new AuditRetentionPolicy(days, Now);

        if (lastRunAt is not null)
        {
            policy.RecordRun(lastRunAt.Value);
        }

        _context.AuditRetentionPolicies.Add(policy);
        await _context.SaveChangesAsync();
    }

    /// <summary>Picks the journal entries of one action.</summary>
    /// <param name="action">The action name.</param>
    /// <returns>The entries written with that action, in order.</returns>
    private List<AuditEntry> Entries(string action)
    {
        return _audit.Written.Where(entry =>
        {
            return entry.Action == action;
        }).ToList();
    }

    /// <summary>The tick seals new rows even when the daily run is not due.</summary>
    [Fact]
    public async Task Every_tick_seals_new_rows_even_when_the_daily_run_is_not_due()
    {
        await SetPolicyAsync(null, Now.AddHours(-1));
        await AddAsync(RowAged(2, "a"), RowAged(1, "b"));

        await TickAsync();

        Assert.All(await _context.AuditEvents.ToListAsync(), row =>
        {
            Assert.NotNull(row.ChainIndex);
        });
        Assert.Empty(Entries(IdentityAuditActions.AuditRetentionRun));
        Assert.Empty(Entries(IdentityAuditActions.AuditChainVerified));
    }

    /// <summary>A run 23 hours 59 minutes ago is not yet due, so nothing daily happens.</summary>
    [Fact]
    public async Task The_daily_run_waits_until_twenty_four_hours_after_the_last_one()
    {
        await SetPolicyAsync(null, Now - AuditMaintenanceHandler.DailyInterval + TimeSpan.FromMinutes(1));

        await TickAsync();

        Assert.Empty(Entries(IdentityAuditActions.AuditRetentionRun));
    }

    /// <summary>Twenty-four hours after the last run the daily work is due again.</summary>
    [Fact]
    public async Task The_daily_run_is_due_exactly_twenty_four_hours_after_the_last_one()
    {
        await SetPolicyAsync(null, Now - AuditMaintenanceHandler.DailyInterval);

        await TickAsync();

        Assert.Single(Entries(IdentityAuditActions.AuditRetentionRun));
    }

    /// <summary>
    /// A fresh panel with retention off still journals its daily run, saying nothing was eligible, and
    /// stores the time so the next tick does not repeat it.
    /// </summary>
    [Fact]
    public async Task A_run_with_retention_off_is_journalled_and_the_time_is_stored()
    {
        await AddAsync(RowAged(500, "old"));

        await TickAsync();
        await TickAsync();

        var run = Assert.Single(Entries(IdentityAuditActions.AuditRetentionRun));
        Assert.True(run.Succeeded);
        Assert.Equal("retentionDays=indefinite cutoff=none purged=0 throughChainIndex=0", run.Subject);
        Assert.Equal(1, await _context.AuditEvents.CountAsync());
        Assert.Equal(Now, Assert.Single(await _context.AuditRetentionPolicies.ToListAsync()).LastRunAt);
        Assert.Null(Assert.Single(await _context.AuditRetentionPolicies.ToListAsync()).RetentionDays);
    }

    /// <summary>
    /// With a window set, one tick seals, purges the aged prefix, journals how much was destroyed, and
    /// verifies a chain that still holds.
    /// </summary>
    [Fact]
    public async Task A_tick_seals_then_purges_then_journals_the_purge_and_verifies_the_rest()
    {
        await SetPolicyAsync(30, null);
        await AddAsync(RowAged(60, "a"), RowAged(50, "b"), RowAged(5, "c"));

        await TickAsync();

        var run = Assert.Single(Entries(IdentityAuditActions.AuditRetentionRun));
        Assert.True(run.Succeeded);
        Assert.Equal(
            "retentionDays=30 cutoff=2026-10-08T12:00:00.0000000Z purged=2 throughChainIndex=2",
            run.Subject);
        Assert.Equal(1, await _context.AuditEvents.CountAsync());

        var verified = Assert.Single(Entries(IdentityAuditActions.AuditChainVerified));
        Assert.True(verified.Succeeded);
        Assert.Equal("intact rowsChecked=1", verified.Subject);
    }

    /// <summary>A retention step that throws is journalled as failed, and the rest of the tick still runs.</summary>
    [Fact]
    public async Task A_retention_failure_is_journalled_as_failed_and_the_other_steps_still_run()
    {
        await SetPolicyAsync(30, null);
        await AddAsync(RowAged(60, "a"), RowAged(5, "b"));

        await TickAsync(new ThrowingClock());

        var run = Assert.Single(Entries(IdentityAuditActions.AuditRetentionRun));
        Assert.False(run.Succeeded);
        Assert.Equal("retentionDays=30 failed", run.Subject);
        Assert.Equal(2, await _context.AuditEvents.CountAsync());
        Assert.Single(Entries(IdentityAuditActions.AuditChainVerified));
        Assert.Equal(Now, Assert.Single(await _context.AuditRetentionPolicies.ToListAsync()).LastRunAt);
    }

    /// <summary>The inverse control: an untouched chain verifies and is journalled as a success.</summary>
    [Fact]
    public async Task A_clean_chain_is_journalled_as_intact()
    {
        await AddAsync(RowAged(3, "a"), RowAged(2, "b"), RowAged(1, "c"));
        await SealNowAsync();

        await TickAsync();

        var verified = Assert.Single(Entries(IdentityAuditActions.AuditChainVerified));
        Assert.True(verified.Succeeded);
        Assert.Equal("intact rowsChecked=3", verified.Subject);
    }

    /// <summary>The positive control: a planted deletion in the middle of the chain is found and journalled as a failure.</summary>
    [Fact]
    public async Task A_broken_chain_is_journalled_as_a_failed_verification()
    {
        var rows = new[] { RowAged(3, "a"), RowAged(2, "b"), RowAged(1, "c") };
        await AddAsync(rows);
        await SealNowAsync();
        _context.AuditEvents.Remove(rows.Single(row =>
        {
            return row.ChainIndex == 2;
        }));
        await _context.SaveChangesAsync();

        await TickAsync();

        var verified = Assert.Single(Entries(IdentityAuditActions.AuditChainVerified));
        Assert.False(verified.Succeeded);
        Assert.StartsWith("broken break=", verified.Subject, StringComparison.Ordinal);
        Assert.Single(Entries(IdentityAuditActions.AuditRetentionRun));
    }

    /// <summary>A clock that refuses to answer, which makes the retention run throw without touching anything else.</summary>
    private sealed class ThrowingClock : IClock
    {
        /// <inheritdoc />
        public DateTimeOffset UtcNow
        {
            get { throw new InvalidOperationException("The clock is unavailable."); }
        }
    }
}
