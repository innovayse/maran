using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Tests.TestSupport;

namespace Maran.Modules.Identity.Tests.Domain.Policies;

/// <summary>Behavioural contract of the audit chain's canonical form and hashes.</summary>
public sealed class AuditChainHasherTests
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(value =>
    {
        return (byte)value;
    }).ToArray();

    private static readonly byte[] Previous = Enumerable.Range(100, 32).Select(value =>
    {
        return (byte)value;
    }).ToArray();

    private static readonly byte[] Salt = Enumerable.Range(50, AuditEvent.SaltLength).Select(value =>
    {
        return (byte)value;
    }).ToArray();

    private static readonly Guid FixedId = new("11111111-1111-1111-1111-111111111111");

    private static readonly Guid FixedActor = new("22222222-2222-2222-2222-222222222222");

    private static AuditEvent Baseline()
    {
        return AuditChainFixture.Row(id: FixedId, actorUserId: FixedActor);
    }

    /// <summary>The same row always produces the same bytes, so a verification can reproduce a sealing.</summary>
    [Fact]
    public void The_same_row_always_has_the_same_canonical_bytes()
    {
        Assert.Equal(AuditCanonicalForm.Immutable(Baseline()), AuditCanonicalForm.Immutable(Baseline()));
        Assert.Equal(AuditCanonicalForm.Erasable(Baseline()), AuditCanonicalForm.Erasable(Baseline()));
    }

    /// <summary>Time below one microsecond is not part of the form, because the database does not keep it.</summary>
    /// <remarks>
    /// The inverse half is the next test: a whole microsecond does change the bytes, so this one is not
    /// satisfied by a form that ignores the instant altogether.
    /// </remarks>
    [Fact]
    public void Time_below_one_microsecond_does_not_change_the_immutable_bytes()
    {
        var whole = AuditChainFixture.Row(AuditChainFixture.Start, id: FixedId, actorUserId: FixedActor);
        var withExtraTicks = AuditChainFixture.Row(AuditChainFixture.Start.AddTicks(9), id: FixedId, actorUserId: FixedActor);

        Assert.Equal(AuditCanonicalForm.Immutable(whole), AuditCanonicalForm.Immutable(withExtraTicks));
    }

    /// <summary>One microsecond of difference does change the immutable bytes.</summary>
    [Fact]
    public void One_microsecond_of_difference_changes_the_immutable_bytes()
    {
        var earlier = AuditChainFixture.Row(AuditChainFixture.Start, id: FixedId, actorUserId: FixedActor);
        var later = AuditChainFixture.Row(AuditChainFixture.Start.AddTicks(TimeSpan.TicksPerMicrosecond), id: FixedId, actorUserId: FixedActor);

        Assert.NotEqual(AuditCanonicalForm.Immutable(earlier), AuditCanonicalForm.Immutable(later));
    }

    /// <summary>The same instant written in another offset has the same bytes: the form is UTC.</summary>
    [Fact]
    public void The_same_instant_in_another_offset_has_the_same_immutable_bytes()
    {
        var utc = AuditChainFixture.Row(AuditChainFixture.Start, id: FixedId, actorUserId: FixedActor);
        var shifted = AuditChainFixture.Row(
            AuditChainFixture.Start.ToOffset(TimeSpan.FromHours(4)), id: FixedId, actorUserId: FixedActor);

        Assert.Equal(AuditCanonicalForm.Immutable(utc), AuditCanonicalForm.Immutable(shifted));
    }

    /// <summary>Each immutable column is part of the immutable form.</summary>
    [Fact]
    public void Each_immutable_column_changes_the_immutable_bytes()
    {
        var baseline = AuditCanonicalForm.Immutable(Baseline());

        var otherId = AuditChainFixture.Row(id: Guid.NewGuid(), actorUserId: FixedActor);
        var otherAction = AuditChainFixture.Row(action: "Logout", id: FixedId, actorUserId: FixedActor);
        var otherTime = AuditChainFixture.Row(AuditChainFixture.Start.AddSeconds(1), id: FixedId, actorUserId: FixedActor);

        Assert.NotEqual(baseline, AuditCanonicalForm.Immutable(otherId));
        Assert.NotEqual(baseline, AuditCanonicalForm.Immutable(otherAction));
        Assert.NotEqual(baseline, AuditCanonicalForm.Immutable(otherTime));
    }

    /// <summary>An outcome flip and a missing correlation id are visible in the immutable form.</summary>
    [Fact]
    public void The_outcome_and_the_correlation_id_are_part_of_the_immutable_form()
    {
        var failed = new AuditEvent(
            FixedId, AuditChainFixture.Start, FixedActor, "alice", "Login", "alice", "203.0.113.7", "agent/1.0",
            succeeded: false, correlationId: "corr-1");
        var uncorrelated = new AuditEvent(
            FixedId, AuditChainFixture.Start, FixedActor, "alice", "Login", "alice", "203.0.113.7", "agent/1.0",
            succeeded: true, correlationId: null);
        var emptyCorrelation = new AuditEvent(
            FixedId, AuditChainFixture.Start, FixedActor, "alice", "Login", "alice", "203.0.113.7", "agent/1.0",
            succeeded: true, correlationId: string.Empty);

        var baseline = AuditCanonicalForm.Immutable(Baseline());

        Assert.NotEqual(baseline, AuditCanonicalForm.Immutable(failed));
        Assert.NotEqual(baseline, AuditCanonicalForm.Immutable(uncorrelated));
        Assert.NotEqual(AuditCanonicalForm.Immutable(uncorrelated), AuditCanonicalForm.Immutable(emptyCorrelation));
    }

    /// <summary>A change to an erasable column leaves the immutable bytes alone (the inverse control).</summary>
    [Fact]
    public void Erasable_columns_are_not_part_of_the_immutable_form()
    {
        var other = AuditChainFixture.Row(
            subject: "someone-else", id: FixedId, actorUserId: Guid.NewGuid(), accountId: Guid.NewGuid());

        Assert.Equal(AuditCanonicalForm.Immutable(Baseline()), AuditCanonicalForm.Immutable(other));
    }

    /// <summary>Each erasable column, the impersonator and the account included, changes the erasable bytes.</summary>
    [Fact]
    public void Each_erasable_column_changes_the_erasable_bytes()
    {
        var baseline = AuditCanonicalForm.Erasable(Baseline());

        var otherActor = AuditChainFixture.Row(id: FixedId, actorUserId: Guid.NewGuid());
        var otherSubject = AuditChainFixture.Row(subject: "bob", id: FixedId, actorUserId: FixedActor);
        var withAccount = AuditChainFixture.Row(id: FixedId, actorUserId: FixedActor, accountId: Guid.NewGuid());
        var withImpersonator = AuditChainFixture.Row(
            id: FixedId, actorUserId: FixedActor, impersonator: new AuditImpersonator(Guid.NewGuid(), "operator"));

        Assert.NotEqual(baseline, AuditCanonicalForm.Erasable(otherActor));
        Assert.NotEqual(baseline, AuditCanonicalForm.Erasable(otherSubject));
        Assert.NotEqual(baseline, AuditCanonicalForm.Erasable(withAccount));
        Assert.NotEqual(baseline, AuditCanonicalForm.Erasable(withImpersonator));
    }

    /// <summary>Moving text across the boundary between two fields changes the bytes.</summary>
    /// <remarks>
    /// Without a length in front of each field, ("ab", "c") and ("a", "bc") are the same bytes and a field
    /// could be shifted into its neighbour unseen. Subject and user agent are neighbours in the form.
    /// </remarks>
    [Fact]
    public void Text_moved_across_a_field_boundary_changes_the_bytes()
    {
        var left = new AuditEvent(
            FixedId, AuditChainFixture.Start, FixedActor, "alice", "Login", "ab", "c", "agent",
            succeeded: true, correlationId: null);
        var right = new AuditEvent(
            FixedId, AuditChainFixture.Start, FixedActor, "alice", "Login", "a", "bc", "agent",
            succeeded: true, correlationId: null);

        Assert.NotEqual(AuditCanonicalForm.Erasable(left), AuditCanonicalForm.Erasable(right));
    }

    /// <summary>The commitment depends on the salt, and a row with no salt commits differently from one with a salt.</summary>
    [Fact]
    public void The_commitment_depends_on_the_salt()
    {
        var row = Baseline();
        var otherSalt = AuditChainHasher.NewSalt();

        var salted = AuditChainHasher.Commitment(row, Salt);

        Assert.Equal(salted, AuditChainHasher.Commitment(row, Salt));
        Assert.NotEqual(salted, AuditChainHasher.Commitment(row, otherSalt));
        Assert.NotEqual(salted, AuditChainHasher.Commitment(row, null));
        Assert.Equal(AuditEvent.HashLength, salted.Length);
    }

    /// <summary>The commitment depends on the erasable columns.</summary>
    [Fact]
    public void The_commitment_depends_on_the_erasable_columns()
    {
        var other = AuditChainFixture.Row(subject: "bob", id: FixedId, actorUserId: FixedActor);

        Assert.NotEqual(AuditChainHasher.Commitment(Baseline(), Salt), AuditChainHasher.Commitment(other, Salt));
    }

    /// <summary>The row hash is reproducible from its inputs and has the documented length.</summary>
    [Fact]
    public void The_row_hash_is_reproducible_from_its_inputs()
    {
        var row = Baseline();
        var commitment = AuditChainHasher.Commitment(row, Salt);

        var hash = AuditChainHasher.RowHash(Key, Previous, row, commitment);

        Assert.Equal(hash, AuditChainHasher.RowHash(Key, Previous, row, commitment));
        Assert.Equal(AuditEvent.HashLength, hash.Length);
    }

    /// <summary>Changing the key, the previous hash, the commitment or an immutable column changes the row hash.</summary>
    [Fact]
    public void Each_input_of_the_row_hash_changes_it()
    {
        var row = Baseline();
        var commitment = AuditChainHasher.Commitment(row, Salt);
        var hash = AuditChainHasher.RowHash(Key, Previous, row, commitment);

        var otherKey = Key.Select(value =>
        {
            return (byte)(value ^ 0xFF);
        }).ToArray();
        var otherPrevious = Previous.Select(value =>
        {
            return (byte)(value ^ 0xFF);
        }).ToArray();
        var otherCommitment = AuditChainHasher.Commitment(row, AuditChainHasher.NewSalt());
        var otherRow = AuditChainFixture.Row(action: "Logout", id: FixedId, actorUserId: FixedActor);

        Assert.NotEqual(hash, AuditChainHasher.RowHash(otherKey, Previous, row, commitment));
        Assert.NotEqual(hash, AuditChainHasher.RowHash(Key, otherPrevious, row, commitment));
        Assert.NotEqual(hash, AuditChainHasher.RowHash(Key, Previous, row, otherCommitment));
        Assert.NotEqual(hash, AuditChainHasher.RowHash(Key, Previous, otherRow, commitment));
    }

    /// <summary>The row hash covers the commitment, not the erasable columns themselves (the inverse control).</summary>
    /// <remarks>
    /// This is what lets an erasure replace the erasable columns without breaking the chain: the hash sees
    /// them only through the commitment, which an erasure does not recompute.
    /// </remarks>
    [Fact]
    public void The_row_hash_does_not_read_the_erasable_columns_directly()
    {
        var row = Baseline();
        var commitment = AuditChainHasher.Commitment(row, Salt);
        var erased = AuditChainFixture.Row(subject: "erased-user-1a2b3c4d", id: FixedId, actorUserId: Guid.NewGuid());

        Assert.Equal(
            AuditChainHasher.RowHash(Key, Previous, row, commitment),
            AuditChainHasher.RowHash(Key, Previous, erased, commitment));
    }

    /// <summary>Equal compares by content, and refuses a value of another length.</summary>
    [Fact]
    public void Equal_compares_by_content_and_by_length()
    {
        byte[] first = [1, 2, 3];

        Assert.True(AuditChainHasher.Equal(first, [1, 2, 3]));
        Assert.False(AuditChainHasher.Equal(first, [1, 2, 4]));
        Assert.False(AuditChainHasher.Equal(first, [1, 2]));
    }

    /// <summary>The genesis hash is the same every time, and a caller changing one copy changes no other.</summary>
    [Fact]
    public void The_genesis_hash_is_stable_and_every_read_is_a_fresh_copy()
    {
        var first = AuditChainHasher.GenesisHash;
        first[0] ^= 0xFF;

        var second = AuditChainHasher.GenesisHash;

        Assert.Equal(AuditEvent.HashLength, second.Length);
        Assert.NotEqual(first, second);
        Assert.Equal(second, AuditChainHasher.GenesisHash);
    }

    /// <summary>With no anchor, or an anchor through index zero, the chain starts at the genesis hash.</summary>
    [Fact]
    public void Without_a_purged_prefix_the_chain_starts_at_the_genesis_hash()
    {
        var emptyAnchor = AuditChainAnchor.Genesis(Previous, AuditChainFixture.Start);

        Assert.Equal(AuditChainHasher.GenesisHash, AuditChainHasher.StartHash(null));
        Assert.Equal(AuditChainHasher.GenesisHash, AuditChainHasher.StartHash(emptyAnchor));
    }

    /// <summary>After a purge, the chain starts at the hash of the last purged row, as a copy.</summary>
    [Fact]
    public void After_a_purge_the_chain_starts_at_the_anchor_hash()
    {
        var anchor = AuditChainAnchor.Genesis(AuditChainHasher.GenesisHash, AuditChainFixture.Start);
        anchor.Advance(5, Previous, AuditChainFixture.Start);

        var start = AuditChainHasher.StartHash(anchor);

        Assert.Equal(Previous, start);
        Assert.NotSame(anchor.AnchorHash, start);
    }

    /// <summary>A new salt has the documented length and is not repeated.</summary>
    [Fact]
    public void A_new_salt_has_the_documented_length_and_is_not_repeated()
    {
        var first = AuditChainHasher.NewSalt();

        Assert.Equal(AuditEvent.SaltLength, first.Length);
        Assert.NotEqual(first, AuditChainHasher.NewSalt());
    }
}
