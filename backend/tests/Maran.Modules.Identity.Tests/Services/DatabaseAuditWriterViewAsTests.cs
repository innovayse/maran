using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>Behavioural contract of the journal's impersonator column (issue #317).</summary>
public sealed class DatabaseAuditWriterViewAsTests : IDisposable
{
    private readonly ViewAsFixture _fixture = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _fixture.Dispose();
    }

    private static AuditEntry Entry(Guid actorId)
    {
        return new AuditEntry(actorId, "acme-owner", IdentityAuditActions.SessionRevoked, "subject", "198.51.100.1", "agent", true);
    }

    /// <summary>An entry written while viewing as a login records the operator beside the actor.</summary>
    [Fact]
    public async Task An_entry_written_while_viewing_records_the_operator()
    {
        var viewedId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var writer = new DatabaseAuditWriter(
            _fixture.Context,
            _fixture.Clock,
            new FakeCorrelationIdAccessor(null),
            new ViewingAsCurrentUser(viewedId, "acme-owner", operatorId, "root-admin"));

        await writer.WriteAsync(Entry(viewedId), CancellationToken.None);

        var stored = await _fixture.Context.AuditEvents.SingleAsync();
        Assert.Equal(viewedId, stored.ActorUserId);
        Assert.Equal(operatorId, stored.Impersonator?.OperatorId);
        Assert.Equal("root-admin", stored.Impersonator?.Username);
    }

    /// <summary>An entry written by somebody acting as themselves has no impersonator.</summary>
    [Fact]
    public async Task An_entry_written_as_oneself_has_no_impersonator()
    {
        var writer = new DatabaseAuditWriter(
            _fixture.Context,
            _fixture.Clock,
            new FakeCorrelationIdAccessor(null),
            new StubCurrentUser());

        await writer.WriteAsync(Entry(Guid.NewGuid()), CancellationToken.None);

        var stored = await _fixture.Context.AuditEvents.SingleAsync();
        Assert.Null(stored.Impersonator);
    }
}
