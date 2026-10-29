using System.Security.Cryptography;
using System.Text;
using Maran.SharedKernel.Security;

namespace Maran.SharedKernel.Tests.Security;

/// <summary>What the key policy refuses: the published sample, structured keys, and one key doing two jobs.</summary>
/// <remarks>
/// Every key is built here from bytes. The only base64 key literal is the published one, so this
/// file does not publish a second sample. "Random" keys are hash output, which is deterministic and
/// has the distribution a generator gives, so no test depends on a draw.
/// </remarks>
public sealed class SecretKeyPolicyTests
{
    /// <summary>The development key this repository publishes, as base64.</summary>
    private const string PublishedKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>A recognised published key answers true.</summary>
    [Fact]
    public void The_published_development_key_is_recognised()
    {
        Assert.True(SecretKeyPolicy.IsPublishedDevelopmentKey(PublishedKey));
    }

    /// <summary>The comparison is on decoded bytes, so whitespace and line breaks do not hide the key.</summary>
    [Fact]
    public void A_published_key_wrapped_in_whitespace_or_line_breaks_is_still_recognised()
    {
        Assert.True(SecretKeyPolicy.IsPublishedDevelopmentKey(" " + PublishedKey + "\n"));
        Assert.True(SecretKeyPolicy.IsPublishedDevelopmentKey(PublishedKey[..20] + "\r\n" + PublishedKey[20..]));
    }

    /// <summary>A key from a generator is not the published one.</summary>
    [Fact]
    public void A_random_key_is_not_a_published_key()
    {
        Assert.False(SecretKeyPolicy.IsPublishedDevelopmentKey(Encode(Derive("not-published"))));
    }

    /// <summary>An absent or malformed value is left to the format checks, so every question answers false.</summary>
    /// <param name="value">The configured value.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64!")]
    [InlineData("====")]
    public void Absent_empty_and_malformed_keys_answer_false_to_every_question_without_throwing(string? value)
    {
        var valid = Encode(Derive("valid"));

        Assert.False(SecretKeyPolicy.IsPublishedDevelopmentKey(value));
        Assert.False(SecretKeyPolicy.IsLowEntropy(value));
        Assert.False(SecretKeyPolicy.AreSameKey(value, value));
        Assert.False(SecretKeyPolicy.AreSameKey(valid, value));
        Assert.False(SecretKeyPolicy.AreSameKey(value, valid));
    }

    /// <summary>Printable text is low entropy even with more than sixteen distinct characters.</summary>
    [Fact]
    public void A_key_that_is_printable_text_is_low_entropy()
    {
        var thirtyTwo = Encoding.ASCII.GetBytes("Tr0ub4dor&3-correct-horse-STAPLE");
        var sixtyFour = Encoding.ASCII.GetBytes(
            "Tr0ub4dor&3-correct-horse-STAPLE" + "Tr0ub4dor&3-correct-horse-staple");

        Assert.Equal(32, thirtyTwo.Length);
        Assert.True(thirtyTwo.Distinct().Count() > 16, "the case must isolate the printable half of the rule");
        Assert.True(SecretKeyPolicy.IsLowEntropy(Encode(thirtyTwo)));
        Assert.True(SecretKeyPolicy.IsLowEntropy(Encode(sixtyFour)));
    }

    /// <summary>A non-printable key of sixteen distinct bytes is a repeated pattern.</summary>
    [Fact]
    public void A_key_with_sixteen_distinct_bytes_is_low_entropy()
    {
        Assert.True(SecretKeyPolicy.IsLowEntropy(Encode(Cycle(0x80, 16, 32))));
    }

    /// <summary>Seventeen distinct non-printable bytes is the accepting side of the boundary.</summary>
    [Fact]
    public void A_key_with_seventeen_distinct_non_printable_bytes_is_not_low_entropy()
    {
        Assert.False(SecretKeyPolicy.IsLowEntropy(Encode(Cycle(0x80, 17, 32))));
    }

    /// <summary>Printable means space through tilde inclusive.</summary>
    [Fact]
    public void The_printable_range_is_space_through_tilde_inclusive()
    {
        var edges = new byte[32];
        for (var i = 0; i < 31; i++)
        {
            edges[i] = (byte)(0x20 + i);
        }

        edges[31] = 0x7E;
        Assert.True(SecretKeyPolicy.IsLowEntropy(Encode(edges)));

        var aboveTilde = (byte[])edges.Clone();
        aboveTilde[31] = 0x7F;
        Assert.False(SecretKeyPolicy.IsLowEntropy(Encode(aboveTilde)));

        var belowSpace = (byte[])edges.Clone();
        belowSpace[0] = 0x1F;
        Assert.False(SecretKeyPolicy.IsLowEntropy(Encode(belowSpace)));
    }

    /// <summary>A false positive stops a correct install, so generator output is never refused.</summary>
    [Fact]
    public void Keys_from_a_uniform_generator_are_never_refused()
    {
        string? previous = null;
        for (var i = 0; i < 10_000; i++)
        {
            var key = Encode(SHA256.HashData(BitConverter.GetBytes(i)));
            Assert.False(SecretKeyPolicy.IsLowEntropy(key));
            Assert.False(SecretKeyPolicy.IsPublishedDevelopmentKey(key));
            Assert.False(SecretKeyPolicy.AreSameKey(previous, key));
            previous = key;
        }

        for (var i = 0; i < 1_000; i++)
        {
            Assert.False(SecretKeyPolicy.IsLowEntropy(Encode(SHA512.HashData(BitConverter.GetBytes(i)))));
        }
    }

    /// <summary>Equal keys are the same key, whatever whitespace the text carries.</summary>
    [Fact]
    public void Two_equal_keys_are_the_same_key()
    {
        var key = Encode(Derive("equal"));

        Assert.True(SecretKeyPolicy.AreSameKey(key, key));
        Assert.True(SecretKeyPolicy.AreSameKey(key, key[..10] + "\n" + key[10..]));
    }

    /// <summary>
    /// A longer key holding the shorter at its start, middle or end gives the shorter away; either argument order.
    /// </summary>
    [Fact]
    public void A_key_that_contains_the_other_key_anywhere_is_the_same_key()
    {
        var inner = Derive("inner");
        var head = Derive("head");
        var tail = Derive("tail");

        var atStart = Encode([.. inner, .. head]);
        var inMiddle = Encode([.. head[..16], .. inner, .. tail[..16]]);
        var atEnd = Encode([.. head, .. inner]);
        var innerText = Encode(inner);

        foreach (var outer in new[] { atStart, inMiddle, atEnd })
        {
            Assert.True(SecretKeyPolicy.AreSameKey(innerText, outer));
            Assert.True(SecretKeyPolicy.AreSameKey(outer, innerText));
        }
    }

    /// <summary>Unrelated keys, one-byte differences and partial overlaps are different keys.</summary>
    [Fact]
    public void Two_independent_keys_are_not_the_same_key()
    {
        var first = Derive("first");
        var second = Derive("second");
        Assert.False(SecretKeyPolicy.AreSameKey(Encode(first), Encode(second)));

        var lastByteDiffers = (byte[])first.Clone();
        lastByteDiffers[31] ^= 0x01;
        Assert.False(SecretKeyPolicy.AreSameKey(Encode(first), Encode(lastByteDiffers)));

        var partial = Encode([.. Derive("padding"), .. Derive("more-padding")[..1], .. first[..31]]);
        Assert.False(SecretKeyPolicy.AreSameKey(Encode(first), partial));
        Assert.False(SecretKeyPolicy.AreSameKey(partial, Encode(first)));
    }

    /// <summary>Derives 32 deterministic, generator-like bytes from a label.</summary>
    /// <param name="label">What distinguishes the key.</param>
    /// <returns>A SHA-256 digest of the label.</returns>
    private static byte[] Derive(string label)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(label));
    }

    /// <summary>Encodes bytes the way a configured key is written.</summary>
    /// <param name="key">The key bytes.</param>
    /// <returns>The base64 text.</returns>
    private static string Encode(byte[] key)
    {
        return Convert.ToBase64String(key);
    }

    /// <summary>Builds a key cycling through consecutive byte values.</summary>
    /// <param name="start">The first value.</param>
    /// <param name="distinct">How many consecutive values to cycle through.</param>
    /// <param name="length">The key length.</param>
    /// <returns>The key bytes.</returns>
    private static byte[] Cycle(int start, int distinct, int length)
    {
        var key = new byte[length];
        for (var i = 0; i < length; i++)
        {
            key[i] = (byte)(start + (i % distinct));
        }

        return key;
    }
}
