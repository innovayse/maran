using Maran.Modules.ServerMigration.Domain.Entities;

namespace Maran.Modules.ServerMigration.Tests.Domain.Entities;

/// <summary>What a pull grant allows, and the moments it stops allowing it (issue #127).</summary>
public sealed class ExportGrantTests
{
    /// <summary>The moment the grants below are issued.</summary>
    private static readonly DateTimeOffset Issued = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A grant of half an hour for the tests to act on.</summary>
    /// <returns>A new grant.</returns>
    private static ExportGrant NewGrant()
    {
        return new ExportGrant(Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), null, Issued, TimeSpan.FromMinutes(30));
    }

    /// <summary>The artifact is read once: the second read of a live grant is refused.</summary>
    [Fact]
    public void The_artifact_can_be_consumed_only_once()
    {
        var grant = NewGrant();

        Assert.True(grant.ConsumeArtifact(Issued.AddMinutes(1)));
        Assert.False(grant.ConsumeArtifact(Issued.AddMinutes(2)));
    }

    /// <summary>The inverse control: a live, unused grant is accepted, so the refusal above is not a grant that refuses everything.</summary>
    [Fact]
    public void A_live_unused_grant_is_accepted_for_the_manifest_and_for_the_artifact()
    {
        var grant = NewGrant();

        Assert.True(grant.ReadManifest(Issued.AddMinutes(1)));
        Assert.True(grant.ConsumeArtifact(Issued.AddMinutes(2)));
    }

    /// <summary>The preview is bounded: the manifest may be read a few times and then no more.</summary>
    [Fact]
    public void The_manifest_can_be_read_only_a_few_times()
    {
        var grant = NewGrant();

        for (var read = 0; read < ExportGrant.MaxManifestReads; read++)
        {
            Assert.True(grant.ReadManifest(Issued.AddMinutes(1)));
        }

        Assert.False(grant.ReadManifest(Issued.AddMinutes(1)));
    }

    /// <summary>A grant that has expired opens nothing.</summary>
    [Fact]
    public void An_expired_grant_opens_nothing()
    {
        var grant = NewGrant();
        var later = Issued.AddMinutes(31);

        Assert.False(grant.IsLive(later));
        Assert.False(grant.ReadManifest(later));
        Assert.False(grant.ConsumeArtifact(later));
    }

    /// <summary>A revoked grant opens nothing, and revoking twice keeps the first time.</summary>
    [Fact]
    public void A_revoked_grant_opens_nothing_and_keeps_the_first_revocation_time()
    {
        var grant = NewGrant();

        grant.Revoke(Issued.AddMinutes(1));
        grant.Revoke(Issued.AddMinutes(5));

        Assert.False(grant.IsLive(Issued.AddMinutes(6)));
        Assert.Equal(Issued.AddMinutes(1), grant.RevokedAt);
    }

    /// <summary>Once the artifact has been read the manifest is no longer served: a half-used grant is a spent one.</summary>
    [Fact]
    public void After_the_artifact_is_consumed_the_manifest_is_refused()
    {
        var grant = NewGrant();
        grant.ConsumeArtifact(Issued.AddMinutes(1));

        Assert.False(grant.ReadManifest(Issued.AddMinutes(2)));
    }
}
