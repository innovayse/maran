using Maran.Agent.Simulated.Usage;

namespace Maran.Agent.Simulated.Tests.Usage;

/// <summary>The hash that makes a figure derived from a name the same on every run.</summary>
public sealed class StableHashTests
{
    /// <summary>The published FNV-1a 64-bit vectors, so the function is the one it claims to be.</summary>
    /// <param name="text">The input.</param>
    /// <param name="expected">The published hash.</param>
    [Theory]
    [InlineData("", 14695981039346656037UL)]
    [InlineData("a", 12638187200555641996UL)]
    [InlineData("foobar", 9625390261332436968UL)]
    public void The_hash_matches_the_published_fnv_1a_vectors(string text, ulong expected)
    {
        Assert.Equal(expected, StableHash.Of(text));
    }

    /// <summary>Different names give different hashes, so accounts do not all show one figure.</summary>
    [Fact]
    public void Different_names_give_different_hashes()
    {
        Assert.NotEqual(StableHash.Of("acme"), StableHash.Of("globex"));
    }

    /// <summary>A missing name is a defect, not an empty hash.</summary>
    [Fact]
    public void A_null_name_is_a_defect()
    {
        Assert.Throws<ArgumentNullException>(() => { return StableHash.Of(null!); });
    }
}
