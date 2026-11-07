using Maran.Modules.Identity.Domain.Entities;

namespace Maran.Modules.Identity.Tests.Domain;

/// <summary>Behavioural contract of the audit chain anchor.</summary>
public sealed class AuditChainAnchorTests
{
    /// <summary>When the anchor was created.</summary>
    private static readonly DateTimeOffset Created = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    /// <summary>When the anchor moved.</summary>
    private static readonly DateTimeOffset Moved = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Builds a chain hash of one repeated byte.</summary>
    /// <param name="value">The byte repeated.</param>
    /// <returns>A hash of the right length.</returns>
    private static byte[] Hash(byte value)
    {
        return Enumerable.Repeat(value, AuditEvent.HashLength).ToArray();
    }

    /// <summary>The genesis anchor sits at position zero on the starting hash and is the singleton.</summary>
    [Fact]
    public void The_genesis_anchor_is_at_position_zero()
    {
        var anchor = AuditChainAnchor.Genesis(Hash(9), Created);

        Assert.Equal(AuditChainAnchor.SingletonId, anchor.Id);
        Assert.Equal(0L, anchor.PurgedThroughIndex);
        Assert.Equal(Hash(9), anchor.AnchorHash);
        Assert.Equal(Created, anchor.UpdatedAt);
    }

    /// <summary>Advancing moves the position, the hash and the time.</summary>
    [Fact]
    public void Advancing_moves_the_anchor_forward()
    {
        var anchor = AuditChainAnchor.Genesis(Hash(9), Created);

        anchor.Advance(500, Hash(7), Moved);

        Assert.Equal(500L, anchor.PurgedThroughIndex);
        Assert.Equal(Hash(7), anchor.AnchorHash);
        Assert.Equal(Moved, anchor.UpdatedAt);
    }

    /// <summary>The anchor never moves backwards or stands still.</summary>
    [Fact]
    public void Advancing_to_the_same_or_an_earlier_position_is_refused()
    {
        var anchor = AuditChainAnchor.Genesis(Hash(9), Created);
        anchor.Advance(500, Hash(7), Moved);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            anchor.Advance(500, Hash(6), Moved);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            anchor.Advance(499, Hash(6), Moved);
        });
        Assert.Equal(500L, anchor.PurgedThroughIndex);
        Assert.Equal(Hash(7), anchor.AnchorHash);
    }

    /// <summary>A hash of the wrong length is refused, at creation and when advancing.</summary>
    [Fact]
    public void A_hash_of_the_wrong_length_is_refused()
    {
        var anchor = AuditChainAnchor.Genesis(Hash(9), Created);

        Assert.Throws<ArgumentException>(() =>
        {
            return AuditChainAnchor.Genesis(new byte[31], Created);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            anchor.Advance(10, new byte[33], Moved);
        });
        Assert.Equal(0L, anchor.PurgedThroughIndex);
    }
}
