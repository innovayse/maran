using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Queries.GetAuditRetention;
using Maran.Modules.Identity.Tests.TestSupport;

namespace Maran.Modules.Identity.Tests.Queries.GetAuditRetention;

/// <summary>Behavioural contract of the audit-retention read.</summary>
public sealed class GetAuditRetentionQueryHandlerTests : IDisposable
{
    /// <summary>The instant the stored row is stamped with.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The in-memory context the handler reads from.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Releases the in-memory context this test owns.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>A panel that never saved a window reads as "keep indefinitely", with the bounds attached.</summary>
    [Fact]
    public async Task A_panel_with_no_row_reads_as_keep_indefinitely_with_the_bounds()
    {
        var result = await new GetAuditRetentionQueryHandler(_context)
            .HandleAsync(new GetAuditRetentionQuery(), CancellationToken.None);

        var dto = result.Value;
        Assert.Null(dto.RetentionDays);
        Assert.Null(dto.LastRunAt);
        Assert.Equal(AuditRetentionPolicy.MinimumDays, dto.MinimumDays);
        Assert.Equal(AuditRetentionPolicy.MaximumDays, dto.MaximumDays);
    }

    /// <summary>A stored window and the time of the last daily run are reported as stored.</summary>
    [Fact]
    public async Task A_stored_window_and_the_last_run_are_reported_as_stored()
    {
        var policy = new AuditRetentionPolicy(120, Now);
        policy.RecordRun(Now.AddHours(1));
        _context.AuditRetentionPolicies.Add(policy);
        await _context.SaveChangesAsync();

        var result = await new GetAuditRetentionQueryHandler(_context)
            .HandleAsync(new GetAuditRetentionQuery(), CancellationToken.None);

        Assert.Equal(120, result.Value.RetentionDays);
        Assert.Equal(Now.AddHours(1), result.Value.LastRunAt);
    }
}
