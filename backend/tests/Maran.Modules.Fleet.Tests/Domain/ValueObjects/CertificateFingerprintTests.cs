using System.Text;
using Maran.Modules.Fleet.Domain.ValueObjects;

namespace Maran.Modules.Fleet.Tests.Domain.ValueObjects;

/// <summary>What counts as a certificate fingerprint, how one is computed and how two are compared.</summary>
public sealed class CertificateFingerprintTests
{
    /// <summary>SHA-256 of the three bytes "abc", the FIPS 180-2 test vector.</summary>
    private const string AbcDigest = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    /// <summary>Sixty-four hexadecimal digits are a fingerprint, held in lowercase whichever case was typed.</summary>
    [Theory]
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
    public void Sixty_four_hexadecimal_digits_are_a_fingerprint_held_in_lowercase(string text)
    {
        var parsed = CertificateFingerprint.TryParse(text, out var fingerprint);

        Assert.True(parsed);
        Assert.NotNull(fingerprint);
        Assert.Equal(AbcDigest, fingerprint.Value);
        Assert.Equal(AbcDigest, fingerprint.ToString());
    }

    /// <summary>Every other spelling is refused rather than guessed at, so what is stored is what is compared.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ba7816bf")]
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015a")]
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ade")]
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ag")]
    [InlineData(" ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("ba:78:16:bf:8f:01:cf:ea:41:41:40:de:5d:ae:22:23:b0:03:61:a3:96:17:7a:9c:b4:10:ff:61:f2:00:15:ad")]
    [InlineData("sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    public void Any_other_spelling_is_refused_without_throwing(string? text)
    {
        var parsed = CertificateFingerprint.TryParse(text, out var fingerprint);

        Assert.False(parsed);
        Assert.Null(fingerprint);
    }

    /// <summary>The fingerprint of a certificate is the SHA-256 of its DER bytes, in lowercase hexadecimal.</summary>
    [Fact]
    public void The_fingerprint_of_der_bytes_is_their_sha256_in_lowercase_hexadecimal()
    {
        var fingerprint = CertificateFingerprint.OfDer(Encoding.ASCII.GetBytes("abc"));

        Assert.Equal(AbcDigest, fingerprint.Value);
    }

    /// <summary>A certificate with no bytes has no fingerprint; hashing nothing would pin an identity that is nobody's.</summary>
    [Fact]
    public void An_empty_certificate_has_no_fingerprint()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return CertificateFingerprint.OfDer(ReadOnlySpan<byte>.Empty);
        });
    }

    /// <summary>Two fingerprints of one digest match, however each was obtained.</summary>
    [Fact]
    public void Two_fingerprints_of_one_digest_match_whether_parsed_or_computed()
    {
        var computed = CertificateFingerprint.OfDer(Encoding.ASCII.GetBytes("abc"));
        Assert.True(CertificateFingerprint.TryParse(AbcDigest.ToUpperInvariant(), out var typed));

        Assert.True(computed.Matches(typed!));
        Assert.True(typed!.Matches(computed));
    }

    /// <summary>Fingerprints that differ in a single digit, first or last, do not match.</summary>
    [Fact]
    public void Fingerprints_that_differ_in_one_digit_do_not_match()
    {
        var original = CertificateFingerprint.OfDer(Encoding.ASCII.GetBytes("abc"));
        var firstDiffers = Parse("0" + AbcDigest[1..]);
        var lastDiffers = Parse(AbcDigest[..^1] + "0");

        Assert.False(original.Matches(firstDiffers));
        Assert.False(original.Matches(lastDiffers));
    }

    /// <summary>A missing fingerprint is a caller bug and is refused, not treated as a mismatch.</summary>
    [Fact]
    public void Comparing_with_nothing_is_refused()
    {
        var original = CertificateFingerprint.OfDer(Encoding.ASCII.GetBytes("abc"));

        Assert.Throws<ArgumentNullException>(() =>
        {
            return original.Matches(null!);
        });
    }

    /// <summary>Parses a fingerprint the test knows is well formed.</summary>
    /// <param name="text">64 hexadecimal digits.</param>
    /// <returns>The fingerprint.</returns>
    private static CertificateFingerprint Parse(string text)
    {
        Assert.True(CertificateFingerprint.TryParse(text, out var fingerprint));
        return fingerprint!;
    }
}
