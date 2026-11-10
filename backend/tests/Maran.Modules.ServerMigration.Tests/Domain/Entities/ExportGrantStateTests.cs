using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;

namespace Maran.Modules.ServerMigration.Tests.Domain.Entities;

/// <summary>Where a pull grant stands at a moment, in the order the list filters by (issue #127).</summary>
public sealed class ExportGrantStateTests
{
    /// <summary>The moment the grants below are issued.</summary>
    private static readonly DateTimeOffset Issued = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A grant of half an hour.</summary>
    /// <returns>A new grant.</returns>
    private static ExportGrant NewGrant()
    {
        return new ExportGrant(Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), null, Issued, TimeSpan.FromMinutes(30));
    }

    /// <summary>An unused grant is live until its time runs out, and expired from then on.</summary>
    [Fact]
    public void An_unused_grant_is_live_and_then_expired()
    {
        var grant = NewGrant();

        Assert.Equal(ExportGrantState.Live, grant.StateAt(Issued.AddMinutes(29)));
        Assert.Equal(ExportGrantState.Expired, grant.StateAt(Issued.AddMinutes(30)));
    }

    /// <summary>A grant whose artifact was read is used, and stays used after its time runs out.</summary>
    [Fact]
    public void A_grant_whose_artifact_was_read_is_used_even_after_it_expires()
    {
        var grant = NewGrant();
        grant.ConsumeArtifact(Issued.AddMinutes(1));

        Assert.Equal(ExportGrantState.Used, grant.StateAt(Issued.AddMinutes(2)));
        Assert.Equal(ExportGrantState.Used, grant.StateAt(Issued.AddHours(5)));
    }

    /// <summary>A revoked grant is revoked whether or not it was used, the fact an administrator did last.</summary>
    [Fact]
    public void A_revoked_grant_is_revoked_before_anything_else()
    {
        var grant = NewGrant();
        grant.ConsumeArtifact(Issued.AddMinutes(1));
        grant.Revoke(Issued.AddMinutes(2));

        Assert.Equal(ExportGrantState.Revoked, grant.StateAt(Issued.AddMinutes(3)));
    }
}
