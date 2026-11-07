using System.Security.Cryptography;
using System.Text;
using Maran.SharedKernel.Security;

namespace Maran.SharedKernel.Tests.Security;

/// <summary>Behavioural contract of <see cref="HkdfSubKeyDeriver"/>.</summary>
public sealed class HkdfSubKeyDeriverTests
{
    /// <summary>A throwaway base64-encoded 256-bit secret, valid only for these tests.</summary>
    private const string Secret = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>A second throwaway 256-bit secret that differs from <see cref="Secret"/>.</summary>
    private const string OtherSecret = "ZmVkY2JhOTg3NjU0MzIxMGZlZGNiYTk4NzY1NDMyMTA=";

    /// <summary>The same purpose under the same secret always gives the same key.</summary>
    [Fact]
    public void The_same_purpose_gives_the_same_key()
    {
        var deriver = new HkdfSubKeyDeriver(Secret);

        Assert.Equal(deriver.DeriveKey("maran/test/v1"), deriver.DeriveKey("maran/test/v1"));
    }

    /// <summary>Two instances built from the same secret agree, so a restart keeps the key.</summary>
    [Fact]
    public void Two_instances_over_the_same_secret_agree()
    {
        Assert.Equal(
            new HkdfSubKeyDeriver(Secret).DeriveKey("maran/test/v1"),
            new HkdfSubKeyDeriver(Secret).DeriveKey("maran/test/v1"));
    }

    /// <summary>Different purposes give different keys.</summary>
    [Fact]
    public void Different_purposes_give_different_keys()
    {
        var deriver = new HkdfSubKeyDeriver(Secret);

        Assert.NotEqual(deriver.DeriveKey("maran/test/v1"), deriver.DeriveKey("maran/test/v2"));
    }

    /// <summary>A different secret gives a different key for the same purpose.</summary>
    [Fact]
    public void A_different_secret_gives_a_different_key()
    {
        Assert.NotEqual(
            new HkdfSubKeyDeriver(Secret).DeriveKey("maran/test/v1"),
            new HkdfSubKeyDeriver(OtherSecret).DeriveKey("maran/test/v1"));
    }

    /// <summary>The derived key is 32 bytes and is not the secret itself.</summary>
    [Fact]
    public void The_derived_key_is_thirty_two_bytes_and_is_not_the_secret()
    {
        var key = new HkdfSubKeyDeriver(Secret).DeriveKey("maran/test/v1");

        Assert.Equal(32, key.Length);
        Assert.NotEqual(Convert.FromBase64String(Secret), key);
    }

    /// <summary>The key is HKDF-SHA256 over the secret with the purpose as the info label and no salt.</summary>
    [Fact]
    public void The_key_is_hkdf_sha256_over_the_secret_with_the_purpose_as_info()
    {
        var expected = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Convert.FromBase64String(Secret),
            32,
            salt: [],
            info: Encoding.UTF8.GetBytes("maran/test/v1"));

        Assert.Equal(expected, new HkdfSubKeyDeriver(Secret).DeriveKey("maran/test/v1"));
    }

    /// <summary>Changing a returned key does not change the next one.</summary>
    [Fact]
    public void Changing_a_returned_key_does_not_affect_the_next_derivation()
    {
        var deriver = new HkdfSubKeyDeriver(Secret);
        var first = deriver.DeriveKey("maran/test/v1");
        var expected = (byte[])first.Clone();

        first[0] ^= 0xFF;

        Assert.Equal(expected, deriver.DeriveKey("maran/test/v1"));
    }

    /// <summary>A missing, empty or blank purpose is refused.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_missing_or_blank_purpose_is_refused(string? purpose)
    {
        var deriver = new HkdfSubKeyDeriver(Secret);

        Assert.ThrowsAny<ArgumentException>(() =>
        {
            return deriver.DeriveKey(purpose!);
        });
    }

    /// <summary>A secret that is not 32 bytes once decoded is refused at construction.</summary>
    [Fact]
    public void A_secret_of_the_wrong_size_is_refused()
    {
        var shortSecret = Convert.ToBase64String(Encoding.UTF8.GetBytes("too-short"));

        Assert.Throws<ArgumentException>(() =>
        {
            return new HkdfSubKeyDeriver(shortSecret);
        });
    }

    /// <summary>A secret that is not valid base64 is refused at construction.</summary>
    [Fact]
    public void A_secret_that_is_not_base64_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return new HkdfSubKeyDeriver("not base64 !!");
        });
    }

    /// <summary>A missing secret is refused at construction.</summary>
    [Fact]
    public void A_missing_secret_is_refused()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            return new HkdfSubKeyDeriver(string.Empty);
        });
    }
}
