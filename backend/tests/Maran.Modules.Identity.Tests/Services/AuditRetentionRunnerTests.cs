using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// Behavioural contract of the audit retention run: it deletes only a sealed prefix of the chain, only
/// rows older than the window, and moves the anchor with every batch it removes.
/// </summary>
public sealed class AuditRetentionRunnerTests : IDisposable
{
    /// <summary>The instant every test treats as the present.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The in-memory context the runner and the sealer share.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    /// <summary>The clock the runner measures its window against.</summary>
    private readonly FakeClock _clock = new(Now);

    /// <summary>Releases the in-memory context this test owns.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Builds an unsealed journal row that happened the given number of days before now.</summary>
    /// <param name="days">How many days ago the row happened.</param>
    /// <param name="subject">The row's subject, so a failing assertion names the row.</param>
    /// <returns>The unsealed row.</returns>
    private static AuditEvent RowAged(double days, string subject)
    {
        return AuditChainFixture.Row(Now.AddDays(-days), subject: subject);
    }

    /// <summary>Builds the runner over this test's context and clock.</summary>
    /// <returns>The runner under test.</returns>
    private AuditRetentionRunner NewRunner()
    {
        return new AuditRetentionRunner(_context, _clock, NullLogger<AuditRetentionRunner>.Instance);
    }

    /// <summary>Stores the retention window.</summary>
    /// <param name="days">The window in days, or null for keep indefinitely.</param>
    /// <returns>Resolves once the row is stored.</returns>
    private async Task SetWindowAsync(int? days)
    {
        _context.AuditRetentionPolicies.Add(new AuditRetentionPolicy(days, Now));
        await _context.SaveChangesAsync();
    }

    /// <summary>Stores the rows and seals them into the chain with the real sealer.</summary>
    /// <param name="rows">The rows to store and seal.</param>
    /// <returns>The same rows in chain-index order.</returns>
    private async Task<List<AuditEvent>> SealAsync(params AuditEvent[] rows)
    {
        _context.AuditEvents.AddRange(rows);
        await _context.SaveChangesAsync();

        await new AuditChainSealer(_context, AuditChainFixture.Deriver())
            .SealAsync(AuditChainSealer.MaxBatchesPerRun, CancellationToken.None);

        return rows.OrderBy(row =>
        {
            return row.ChainIndex;
        }).ToList();
    }

    /// <summary>A panel that never saved a window keeps everything, and says there was no cutoff.</summary>
    [Fact]
    public async Task Without_a_policy_row_nothing_is_purged()
    {
        await SealAsync(RowAged(90, "old"));

        var outcome = await NewRunner().RunAsync(CancellationToken.None);

        Assert.Equal(0, outcome.Purged);
        Assert.Null(outcome.Cutoff);
        Assert.Equal(1, await _context.AuditEvents.CountAsync());
        Assert.Empty(await _context.AuditChainAnchors.ToListAsync());
    }

    /// <summary>A window of "keep indefinitely" purges nothing however old the rows are.</summary>
    [Fact]
    public async Task With_retention_off_nothing_is_purged_however_old_the_rows_are()
    {
        await SetWindowAsync(null);
        await SealAsync(RowAged(3000, "ancient"), RowAged(2000, "old"));

        var outcome = await NewRunner().RunAsync(CancellationToken.None);

        Assert.Equal(0, outcome.Purged);
        Assert.Null(outcome.Cutoff);
        Assert.Equal(2, await _context.AuditEvents.CountAsync());
    }

    /// <summary>
    /// The purge runs through one less than the smallest sealed index inside the window: a row older than
    /// the cutoff that committed late carries a higher index and is kept, and the anchor takes the hash of
    /// the last row removed.
    /// </summary>
    [Fact]
    public async Task Only_the_sealed_prefix_is_purged_and_a_late_committer_is_kept()
    {
        await SetWindowAsync(30);
        var first = await SealAsync(RowAged(60, "a"), RowAged(50, "b"), RowAged(40, "c"), RowAged(10, "d"));
        var late = (await SealAsync(RowAged(45, "late")))[0];
        var hashOfThird = first[2].RowHash!.ToArray();

        var outcome = await NewRunner().RunAsync(CancellationToken.None);

        Assert.Equal(3, outcome.Purged);
        Assert.Equal(3, outcome.ThroughChainIndex);
        Assert.Equal(Now.AddDays(-30), outcome.Cutoff);
        Assert.Equal(new long?[] { 4, 5 }, (await _context.AuditEvents.OrderBy(row => row.ChainIndex).ToListAsync())
            .Select(row =>
            {
                return row.ChainIndex;
            }));
        Assert.Equal(5, late.ChainIndex);

        var anchor = Assert.Single(await _context.AuditChainAnchors.ToListAsync());
        Assert.Equal(3, anchor.PurgedThroughIndex);
        Assert.Equal(hashOfThird, anchor.AnchorHash);
    }

    /// <summary>An old row that was never sealed has no chain index and is never selected.</summary>
    [Fact]
    public async Task An_unsealed_old_row_is_never_purged()
    {
        await SetWindowAsync(30);
        await SealAsync(RowAged(60, "sealed-a"), RowAged(50, "sealed-b"));
        var unsealed = RowAged(70, "unsealed");
        _context.AuditEvents.Add(unsealed);
        await _context.SaveChangesAsync();

        var outcome = await NewRunner().RunAsync(CancellationToken.None);

        Assert.Equal(2, outcome.Purged);
        var remaining = Assert.Single(await _context.AuditEvents.ToListAsync());
        Assert.Equal(unsealed.Id, remaining.Id);
        Assert.Null(remaining.ChainIndex);
    }

    /// <summary>
    /// When every sealed row is older than the window the whole sealed chain goes, and the chain carries on
    /// from the anchor: the next sealed row continues the index and a verification is still intact.
    /// </summary>
    [Fact]
    public async Task A_purge_of_the_whole_sealed_chain_leaves_a_chain_that_still_verifies()
    {
        await SetWindowAsync(30);
        await SealAsync(RowAged(60, "a"), RowAged(50, "b"));

        var outcome = await NewRunner().RunAsync(CancellationToken.None);
        var next = (await SealAsync(RowAged(1, "fresh")))[0];
        var verdict = await AuditChainFixture.Verifier(_context).VerifyAsync(CancellationToken.None);

        Assert.Equal(2, outcome.Purged);
        Assert.Equal(2, outcome.ThroughChainIndex);
        Assert.Equal(3, next.ChainIndex);
        Assert.True(verdict.Intact);
        Assert.Equal(AuditChainBreak.None, verdict.Break);
        Assert.Equal(1, verdict.RowsChecked);
    }

    /// <summary>A partial purge leaves a chain that verifies from the anchor it advanced.</summary>
    [Fact]
    public async Task A_partial_purge_leaves_a_chain_that_still_verifies_from_the_anchor()
    {
        await SetWindowAsync(30);
        await SealAsync(RowAged(60, "a"), RowAged(50, "b"), RowAged(10, "c"), RowAged(5, "d"));

        await NewRunner().RunAsync(CancellationToken.None);
        var verdict = await AuditChainFixture.Verifier(_context).VerifyAsync(CancellationToken.None);

        Assert.True(verdict.Intact);
        Assert.Equal(2, verdict.RowsChecked);
    }

    /// <summary>A second run with nothing newly eligible removes nothing and reports the anchor it already holds.</summary>
    [Fact]
    public async Task A_second_run_purges_nothing_and_keeps_the_anchor()
    {
        await SetWindowAsync(30);
        await SealAsync(RowAged(60, "a"), RowAged(50, "b"), RowAged(10, "c"));
        await NewRunner().RunAsync(CancellationToken.None);

        var outcome = await NewRunner().RunAsync(CancellationToken.None);

        Assert.Equal(0, outcome.Purged);
        Assert.Equal(2, outcome.ThroughChainIndex);
        Assert.Equal(1, await _context.AuditEvents.CountAsync());
        Assert.Equal(2, Assert.Single(await _context.AuditChainAnchors.ToListAsync()).PurgedThroughIndex);
    }

    /// <summary>
    /// A hole in the sealed range is a doubt, and a doubt purges nothing: removing a prefix that contains
    /// a gap would advance the anchor over the evidence of a break.
    /// </summary>
    [Fact]
    public async Task A_gap_in_the_chain_stops_the_run_before_anything_is_removed()
    {
        await SetWindowAsync(30);
        var rows = await SealAsync(RowAged(60, "a"), RowAged(50, "b"), RowAged(40, "c"), RowAged(35, "d"));
        _context.AuditEvents.Remove(rows[1]);
        await _context.SaveChangesAsync();

        var outcome = await NewRunner().RunAsync(CancellationToken.None);

        Assert.Equal(0, outcome.Purged);
        Assert.Equal(3, await _context.AuditEvents.CountAsync());
        Assert.Empty(await _context.AuditChainAnchors.ToListAsync());
    }

    /// <summary>A purge larger than one batch is removed in full, with the anchor at the last row.</summary>
    [Fact]
    public async Task A_purge_larger_than_one_batch_is_removed_in_full()
    {
        await SetWindowAsync(30);
        var total = AuditRetentionRunner.PurgeBatchSize + 1;
        var rows = Enumerable.Range(0, total)
            .Select(index =>
            {
                return RowAged(400 - (index / 100.0), "old-" + index);
            })
            .Append(RowAged(1, "fresh"))
            .ToArray();
        await SealAsync(rows);

        var outcome = await NewRunner().RunAsync(CancellationToken.None);

        Assert.Equal(total, outcome.Purged);
        Assert.Equal(total, outcome.ThroughChainIndex);
        Assert.Equal(1, await _context.AuditEvents.CountAsync());
        Assert.Equal(total, Assert.Single(await _context.AuditChainAnchors.ToListAsync()).PurgedThroughIndex);
    }
}
