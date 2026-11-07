using Maran.Modules.Identity.Commands.VerifyAuditChain;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Commands.VerifyAuditChain;

/// <summary>Behavioural contract of the handler that verifies the audit chain on an administrator's request.</summary>
public sealed class VerifyAuditChainCommandHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly RecordingAuditWriter _audit = new();
    private readonly StubCurrentUser _administrator = new();

    /// <summary>Releases the in-memory context this test owns.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private VerifyAuditChainCommandHandler NewHandler()
    {
        return new VerifyAuditChainCommandHandler(
            AuditChainFixture.Verifier(_context),
            new IdentityAuditJournal(_audit, _administrator),
            _administrator,
            new FakeClock(Now));
    }

    private static VerifyAuditChainCommand Command()
    {
        return new VerifyAuditChainCommand("203.0.113.7", "agent");
    }

    /// <summary>An intact chain is reported as intact and journalled as a success by the administrator.</summary>
    [Fact]
    public async Task An_intact_chain_is_reported_and_journalled_as_a_success()
    {
        await AuditChainFixture.SeedSealedAsync(_context, 3);

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Intact);
        Assert.Equal(3, result.Value.RowsChecked);
        Assert.Equal(nameof(AuditChainBreak.None), result.Value.Break);
        Assert.Equal(Now, result.Value.VerifiedAt);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.AuditChainVerified, entry.Action);
        Assert.True(entry.Succeeded);
        Assert.Equal(_administrator.UserId, entry.ActorUserId);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        Assert.Equal("intact rowsChecked=3", entry.Subject);
    }

    /// <summary>A broken chain is a successful answer that says so, and the journal entry is marked failed.</summary>
    [Fact]
    public async Task A_broken_chain_is_reported_and_journalled_as_a_failure_naming_where()
    {
        var rows = await AuditChainFixture.SeedSealedAsync(_context, 3);
        _context.Entry(rows[1]).Property(row => row.Action).CurrentValue = "Logout";
        await _context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Intact);
        Assert.Equal(2, result.Value.FirstBrokenChainIndex);
        Assert.Equal(nameof(AuditChainBreak.RowHashMismatch), result.Value.Break);

        var entry = Assert.Single(_audit.Written);
        Assert.False(entry.Succeeded);
        Assert.Equal("broken break=RowHashMismatch firstBrokenChainIndex=2 rowsChecked=2", entry.Subject);
    }
}
