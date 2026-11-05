using Maran.Modules.Sftp.Domain.ValueObjects;
using Maran.Modules.Sftp.Tests.TestSupport;

namespace Maran.Modules.Sftp.Tests.Domain.ValueObjects;

/// <summary>
/// What <see cref="SftpPublicKey.Parse"/> accepts and what it makes of it: the positive controls
/// for the refusals in <see cref="SftpPublicKeyRefusalTests"/>, and the fingerprint OpenSSH itself
/// reports for each key.
/// </summary>
public sealed class SftpPublicKeyTests
{
    /// <summary>A key of every allow-listed algorithm that can be generated is accepted, with the fingerprint OpenSSH prints.</summary>
    /// <param name="line">The key as <c>ssh-keygen</c> wrote it.</param>
    /// <param name="algorithm">The algorithm the key must be reported under.</param>
    /// <param name="fingerprint">The fingerprint <c>ssh-keygen -lf -E sha256</c> printed for it.</param>
    [Theory]
    [InlineData(SftpTestKeys.Ed25519, "ssh-ed25519", SftpTestKeys.Ed25519Fingerprint)]
    [InlineData(SftpTestKeys.P256, "ecdsa-sha2-nistp256", SftpTestKeys.P256Fingerprint)]
    [InlineData(SftpTestKeys.P384, "ecdsa-sha2-nistp384", SftpTestKeys.P384Fingerprint)]
    [InlineData(SftpTestKeys.P521, "ecdsa-sha2-nistp521", SftpTestKeys.P521Fingerprint)]
    [InlineData(SftpTestKeys.Rsa2048, "ssh-rsa", SftpTestKeys.Rsa2048Fingerprint)]
    [InlineData(SftpTestKeys.Rsa3072, "ssh-rsa", SftpTestKeys.Rsa3072Fingerprint)]
    public void A_generated_key_is_accepted_with_the_fingerprint_openssh_prints(
        string line,
        string algorithm,
        string fingerprint)
    {
        var parsed = SftpPublicKey.Parse(line);

        Assert.True(parsed.IsSuccess);
        Assert.Equal(algorithm, parsed.Value.Algorithm);
        Assert.Equal(fingerprint, parsed.Value.Fingerprint);
        Assert.Equal(line, parsed.Value.Line);
    }

    /// <summary>The hardware-backed algorithms are accepted when the blob has the layout they require.</summary>
    [Fact]
    public void The_hardware_backed_algorithms_are_accepted_when_the_blob_has_the_layout_they_require()
    {
        var ed25519 = SftpPublicKey.Parse(SftpTestKeys.SkEd25519());
        var ecdsa = SftpPublicKey.Parse(SftpTestKeys.SkEcdsaP256());

        Assert.True(ed25519.IsSuccess);
        Assert.Equal("sk-ssh-ed25519@openssh.com", ed25519.Value.Algorithm);
        Assert.True(ecdsa.IsSuccess);
        Assert.Equal("sk-ecdsa-sha2-nistp256@openssh.com", ecdsa.Value.Algorithm);
    }

    /// <summary>An RSA key of exactly 2048 bits is accepted, the agent's floor.</summary>
    [Fact]
    public void An_rsa_key_of_exactly_2048_bits_is_accepted()
    {
        Assert.True(SftpPublicKey.Parse(SftpTestKeys.RsaOfBits(2048)).IsSuccess);
    }

    /// <summary>An RSA key of exactly 16384 bits is accepted, the agent's ceiling.</summary>
    [Fact]
    public void An_rsa_key_of_exactly_16384_bits_is_accepted()
    {
        Assert.True(SftpPublicKey.Parse(SftpTestKeys.RsaOfBits(16384)).IsSuccess);
    }

    /// <summary>An RSA modulus written with a leading zero byte is measured without it.</summary>
    [Fact]
    public void An_rsa_modulus_written_with_a_leading_zero_byte_is_measured_without_it()
    {
        // The wire form of a positive mpint whose top bit is set carries one zero byte in front; the
        // size is the size of the number, not of the field.
        var modulus = new byte[257];
        Array.Fill(modulus, (byte)0xAB);
        modulus[0] = 0x00;
        modulus[1] = 0x80;
        var blob = SftpTestKeys.Concatenate(
            SftpTestKeys.WireString("ssh-rsa"),
            SftpTestKeys.WireBytes([0x01, 0x00, 0x01]),
            SftpTestKeys.WireBytes(modulus));

        var parsed = SftpPublicKey.Parse(SftpTestKeys.FromBlob("ssh-rsa", blob));

        Assert.True(parsed.IsSuccess);
    }

    /// <summary>The comment is dropped, so the stored line and the fingerprint are those of the bare key.</summary>
    [Fact]
    public void The_comment_is_dropped_so_the_stored_line_is_the_bare_key()
    {
        var parsed = SftpPublicKey.Parse(SftpTestKeys.Ed25519 + " alice@laptop with spaces");

        Assert.True(parsed.IsSuccess);
        Assert.Equal(SftpTestKeys.Ed25519, parsed.Value.Line);
        Assert.Equal(SftpTestKeys.Ed25519Fingerprint, parsed.Value.Fingerprint);
        Assert.DoesNotContain("alice", parsed.Value.Line, StringComparison.Ordinal);
    }

    /// <summary>Spaces around the pasted text and repeated spaces between its fields are ignored.</summary>
    [Fact]
    public void Spaces_around_the_pasted_text_and_between_its_fields_are_ignored()
    {
        var spaced = "   " + SftpTestKeys.Ed25519.Replace(" ", "    ", StringComparison.Ordinal) + "   ";

        var parsed = SftpPublicKey.Parse(spaced);

        Assert.True(parsed.IsSuccess);
        Assert.Equal(SftpTestKeys.Ed25519, parsed.Value.Line);
    }

    /// <summary>A key whose pasted text is exactly the size limit is accepted.</summary>
    [Fact]
    public void A_key_whose_text_is_exactly_the_size_limit_is_accepted()
    {
        // Padded with a comment up to 8 KiB to the byte: the limit is inclusive, and this is the
        // control for the oversize refusal one byte above it.
        var padding = SftpPublicKey.MaxLength - SftpTestKeys.Ed25519.Length - 1;
        var text = SftpTestKeys.Ed25519 + " " + new string('c', padding);

        Assert.Equal(SftpPublicKey.MaxLength, text.Length);
        Assert.True(SftpPublicKey.Parse(text).IsSuccess);
    }

    /// <summary>Different keys give different fingerprints and one key always gives the same.</summary>
    [Fact]
    public void Different_keys_give_different_fingerprints_and_one_key_always_gives_the_same()
    {
        var first = SftpPublicKey.Parse(SftpTestKeys.Distinct(1)).Value.Fingerprint;
        var again = SftpPublicKey.Parse(SftpTestKeys.Distinct(1) + " other-comment").Value.Fingerprint;
        var second = SftpPublicKey.Parse(SftpTestKeys.Distinct(2)).Value.Fingerprint;

        Assert.Equal(first, again);
        Assert.NotEqual(first, second);
    }

    /// <summary>Every fingerprint has the prefix, no base64 padding, and fits the documented maximum.</summary>
    [Fact]
    public void Every_fingerprint_has_the_prefix_no_padding_and_fits_the_documented_maximum()
    {
        var fingerprint = SftpPublicKey.Parse(SftpTestKeys.P521).Value.Fingerprint;

        Assert.StartsWith("SHA256:", fingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain("=", fingerprint, StringComparison.Ordinal);
        Assert.Equal(SftpPublicKey.MaxFingerprintLength, fingerprint.Length);
    }

    /// <summary>No accepted algorithm name is longer than the documented maximum.</summary>
    [Fact]
    public void No_accepted_algorithm_name_is_longer_than_the_documented_maximum()
    {
        var longest = new[]
        {
            SftpTestKeys.SkEd25519(), SftpTestKeys.SkEcdsaP256(), SftpTestKeys.P521, SftpTestKeys.Ed25519,
        }.Select(line =>
        {
            return SftpPublicKey.Parse(line).Value.Algorithm.Length;
        }).Max();

        Assert.Equal(SftpPublicKey.MaxAlgorithmLength, longest);
    }
}
