using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Queries.GetAuditChainStatus;
using Maran.Modules.Identity.Tests.TestSupport;

namespace Maran.Modules.Identity.Tests.Queries.GetAuditChainStatus;

/// <summary>Behavioural contract of the handler that reports how far the journal is sealed.</summary>
public sealed class GetAuditChainStatusQueryHandlerTests : IDisposable
{
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Releases the in-memory context this test owns.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private async Task<AuditChainStatusDto> ReadAsync()
    {
        var result = await new GetAuditChainStatusQueryHandler(_context)
            .HandleAsync(new GetAuditChainStatusQuery(), CancellationToken.None);

        return result.Value;
    }

    /// <summary>An empty journal reports zero everywhere and no newest position.</summary>
    [Fact]
    public async Task An_empty_journal_reports_nothing_sealed()
    {
        var status = await ReadAsync();

        Assert.Equal(0, status.SealedRows);
        Assert.Equal(0, status.UnsealedRows);
        Assert.Null(status.LastChainIndex);
        Assert.Null(status.LastSealedAt);
        Assert.Equal(0, status.PurgedThroughIndex);
    }

    /// <summary>Sealed and unsealed rows are counted apart, and the newest sealed position is reported.</summary>
    [Fact]
    public async Task Sealed_and_unsealed_rows_are_counted_apart()
    {
        await AuditChainFixture.SeedSealedAsync(_context, 3);
        await AuditChainFixture.SeedAsync(_context, 2);

        var status = await ReadAsync();

        Assert.Equal(3, status.SealedRows);
        Assert.Equal(2, status.UnsealedRows);
        Assert.Equal(3, status.LastChainIndex);
        Assert.Equal(AuditChainFixture.Start.AddMinutes(2), status.LastSealedAt);
    }

    /// <summary>The position retention has purged through is reported from the anchor.</summary>
    [Fact]
    public async Task The_purge_point_is_read_from_the_anchor()
    {
        var anchor = AuditChainAnchor.Genesis(AuditChainHasher.GenesisHash, AuditChainFixture.Start);
        anchor.Advance(42, AuditChainHasher.GenesisHash, AuditChainFixture.Start);
        _context.AuditChainAnchors.Add(anchor);
        await _context.SaveChangesAsync();

        var status = await ReadAsync();

        Assert.Equal(42, status.PurgedThroughIndex);
    }
}
