using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Mappers;

namespace Maran.Modules.Identity.Tests.Mappers;

/// <summary>Behavioural contract of <see cref="AuditExportRowMapper"/>: a journal row restated as the export's fourteen columns.</summary>
public sealed class AuditExportRowMapperTests
{
    /// <summary>Every column of an unsealed row with an operator and an account is carried over.</summary>
    [Fact]
    public void Every_column_of_an_unsealed_row_is_carried_over()
    {
        var id = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var account = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 8, 30, 14, 0, 0, 123, TimeSpan.FromHours(2)).AddTicks(4560);
        var auditEvent = new AuditEvent(
            id, at, actor, "olive", "SiteCreated", "shop.example.com", "203.0.113.7", "agent", false, "corr-1",
            new AuditImpersonator(operatorId, "root"), account);

        var row = AuditExportRowMapper.From(auditEvent);

        Assert.Equal(id, row.Id);
        Assert.Equal("2026-08-30T12:00:00.123456Z", row.OccurredAt);
        Assert.Equal(actor, row.ActorUserId);
        Assert.Equal("olive", row.ActorUsername);
        Assert.Equal("root", row.ImpersonatorUsername);
        Assert.Equal("SiteCreated", row.Action);
        Assert.Equal("failed", row.Outcome);
        Assert.Equal("shop.example.com", row.Subject);
        Assert.Equal("203.0.113.7", row.IpAddress);
        Assert.Equal("agent", row.UserAgent);
        Assert.Equal(account, row.AccountId);
        Assert.Equal("corr-1", row.CorrelationId);
        Assert.Null(row.ChainIndex);
        Assert.Null(row.RowHash);
    }

    /// <summary>A succeeded row reads <c>succeeded</c> and a sealed row carries its index and its hash in lower-case hex.</summary>
    [Fact]
    public void A_sealed_succeeded_row_carries_its_chain_index_and_a_lower_case_hex_hash()
    {
        var auditEvent = new AuditEvent(
            Guid.NewGuid(), DateTimeOffset.UnixEpoch, null, "olive", "SiteCreated", "s", "ip", "ua", true, null);
        auditEvent.Seal(5, new byte[AuditEvent.HashLength], new byte[AuditEvent.SaltLength], Enumerable.Repeat((byte)0xAB, AuditEvent.HashLength).ToArray());

        var row = AuditExportRowMapper.From(auditEvent);

        Assert.Equal("succeeded", row.Outcome);
        Assert.Equal(5, row.ChainIndex);
        Assert.Equal("abab", row.RowHash![..4]);
        Assert.Equal(AuditEvent.HashLength * 2, row.RowHash.Length);
        Assert.Equal(row.RowHash.ToLowerInvariant(), row.RowHash);
        Assert.Null(row.ImpersonatorUsername);
    }
}
