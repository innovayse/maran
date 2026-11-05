using Maran.Modules.Sftp.Domain.ValueObjects;
using Maran.Modules.Sftp.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Sftp.Tests.Domain.ValueObjects;

/// <summary>
/// Everything <see cref="SftpPublicKey.Parse"/> refuses, each case next to the accepted control in
/// <see cref="SftpPublicKeyTests"/> that differs from it in the one respect named.
/// </summary>
public sealed class SftpPublicKeyRefusalTests
{
    /// <summary>The key every mutation below starts from; it is accepted unmodified.</summary>
    private const string Good = SftpTestKeys.Ed25519;

    /// <summary>The unmodified key is accepted, so each refusal below is caused by its one mutation.</summary>
    [Fact]
    public void The_unmodified_key_every_refusal_starts_from_is_accepted()
    {
        Assert.True(SftpPublicKey.Parse(Good).IsSuccess);
    }

    /// <summary>Text that is absent, empty or only spaces is refused.</summary>
    /// <param name="text">The pasted text.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Absent_empty_and_blank_text_is_refused(string? text)
    {
        AssertRefused(text);
    }

    /// <summary>A line with an authorized_keys option in front of the key is refused, whatever the option.</summary>
    /// <param name="option">The option text placed before the key.</param>
    [Theory]
    [InlineData("command=\"/bin/true\"")]
    [InlineData("no-pty,no-port-forwarding")]
    [InlineData("from=\"203.0.113.7\"")]
    [InlineData("restrict")]
    [InlineData("environment=\"A=b\"")]
    public void A_line_with_an_option_in_front_of_the_key_is_refused(string option)
    {
        AssertRefused(option + " " + Good);
    }

    /// <summary>A second line, however it is separated, is refused rather than ignored.</summary>
    /// <param name="separator">What sits between the first key and the second.</param>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void A_second_line_is_refused_rather_than_ignored(string separator)
    {
        AssertRefused(Good + separator + SftpTestKeys.P256);
    }

    /// <summary>A trailing newline is refused: it is the start of a second line.</summary>
    [Fact]
    public void A_trailing_newline_is_refused()
    {
        AssertRefused(Good + "\n");
    }

    /// <summary>A control character in the key or in its comment is refused.</summary>
    /// <param name="control">The control character, as a code point.</param>
    [Theory]
    [InlineData(0x00)]
    [InlineData(0x09)]
    [InlineData(0x0B)]
    [InlineData(0x1B)]
    [InlineData(0x7F)]
    [InlineData(0x85)]
    public void A_control_character_anywhere_is_refused(int control)
    {
        var character = ((char)control).ToString();

        AssertRefused(Good + " comment" + character + "tail");
        AssertRefused(character + Good);
        AssertRefused(Good.Replace(" ", character, StringComparison.Ordinal));
    }

    /// <summary>A declared algorithm outside the allow-list is refused, <c>ssh-dss</c> included.</summary>
    /// <param name="algorithm">The first field.</param>
    [Theory]
    [InlineData("ssh-dss")]
    [InlineData("ssh-foo")]
    [InlineData("SSH-ED25519")]
    [InlineData("ssh-ed25519-cert-v01@openssh.com")]
    public void A_declared_algorithm_outside_the_allow_list_is_refused(string algorithm)
    {
        AssertRefused(algorithm + " " + Good.Split(' ')[1]);
    }

    /// <summary>A key with no second field is refused.</summary>
    [Fact]
    public void A_key_with_no_second_field_is_refused()
    {
        AssertRefused("ssh-ed25519");
    }

    /// <summary>A declared algorithm that the blob contradicts is refused, in every direction tried.</summary>
    [Fact]
    public void A_declared_algorithm_that_the_blob_contradicts_is_refused()
    {
        var ed25519Data = Good.Split(' ')[1];
        var rsaData = SftpTestKeys.Rsa2048.Split(' ')[1];
        var p256Data = SftpTestKeys.P256.Split(' ')[1];
        var p384Data = SftpTestKeys.P384.Split(' ')[1];

        AssertRefused("ssh-rsa " + ed25519Data);
        AssertRefused("ssh-ed25519 " + rsaData);
        AssertRefused("ecdsa-sha2-nistp384 " + p256Data);
        AssertRefused("ecdsa-sha2-nistp256 " + p384Data);
        AssertRefused("ecdsa-sha2-nistp256 " + ed25519Data);
    }

    /// <summary>An RSA key below 2048 bits or above 16384 bits is refused, one bit outside either bound.</summary>
    /// <param name="bits">The modulus size.</param>
    [Theory]
    [InlineData(1024)]
    [InlineData(2047)]
    [InlineData(16385)]
    public void An_rsa_key_outside_2048_to_16384_bits_is_refused(int bits)
    {
        AssertRefused(SftpTestKeys.RsaOfBits(bits));
    }

    /// <summary>A real 1024-bit RSA key from ssh-keygen is refused.</summary>
    [Fact]
    public void A_real_1024_bit_rsa_key_is_refused()
    {
        AssertRefused(SftpTestKeys.Rsa1024);
    }

    /// <summary>An RSA key whose exponent or modulus is empty is refused.</summary>
    [Fact]
    public void An_rsa_key_with_an_empty_exponent_or_modulus_is_refused()
    {
        var emptyExponent = SftpTestKeys.Concatenate(
            SftpTestKeys.WireString("ssh-rsa"),
            SftpTestKeys.WireBytes([]),
            SftpTestKeys.WireBytes(Enumerable.Repeat((byte)0xAB, 256).ToArray()));
        var emptyModulus = SftpTestKeys.Concatenate(
            SftpTestKeys.WireString("ssh-rsa"),
            SftpTestKeys.WireBytes([0x01, 0x00, 0x01]),
            SftpTestKeys.WireBytes([]));

        AssertRefused(SftpTestKeys.FromBlob("ssh-rsa", emptyExponent));
        AssertRefused(SftpTestKeys.FromBlob("ssh-rsa", emptyModulus));
    }

    /// <summary>Bytes after a well-formed key blob are refused, for every algorithm.</summary>
    /// <param name="line">A well-formed key.</param>
    [Theory]
    [InlineData(SftpTestKeys.Ed25519)]
    [InlineData(SftpTestKeys.P256)]
    [InlineData(SftpTestKeys.P384)]
    [InlineData(SftpTestKeys.P521)]
    [InlineData(SftpTestKeys.Rsa2048)]
    public void Trailing_bytes_after_the_blob_are_refused(string line)
    {
        var algorithm = line.Split(' ')[0];
        var blob = SftpTestKeys.BlobOf(line);

        Assert.True(SftpPublicKey.Parse(line).IsSuccess);
        AssertRefused(SftpTestKeys.FromBlob(algorithm, [.. blob, 0x00]));
        AssertRefused(SftpTestKeys.FromBlob(algorithm, [.. blob, .. SftpTestKeys.WireString("smuggled")]));
    }

    /// <summary>A blob cut short, so a field promises more bytes than remain, is refused.</summary>
    /// <param name="line">A well-formed key.</param>
    [Theory]
    [InlineData(SftpTestKeys.Ed25519)]
    [InlineData(SftpTestKeys.P256)]
    [InlineData(SftpTestKeys.Rsa2048)]
    public void A_truncated_blob_is_refused(string line)
    {
        var algorithm = line.Split(' ')[0];
        var blob = SftpTestKeys.BlobOf(line);

        AssertRefused(SftpTestKeys.FromBlob(algorithm, blob[..^1]));
        AssertRefused(SftpTestKeys.FromBlob(algorithm, blob[..6]));
    }

    /// <summary>A key of the wrong length or point form for its algorithm is refused.</summary>
    [Fact]
    public void A_key_of_the_wrong_length_or_point_form_for_its_algorithm_is_refused()
    {
        var shortEd25519 = SftpTestKeys.Concatenate(
            SftpTestKeys.WireString("ssh-ed25519"),
            SftpTestKeys.WireBytes(new byte[31]));
        var compressedPoint = new byte[65];
        compressedPoint[0] = 0x02;
        var compressed = SftpTestKeys.Concatenate(
            SftpTestKeys.WireString("ecdsa-sha2-nistp256"),
            SftpTestKeys.WireString("nistp256"),
            SftpTestKeys.WireBytes(compressedPoint));
        var wrongCurve = SftpTestKeys.BlobOf(SftpTestKeys.P256).ToArray();
        // "nistp256" inside the blob becomes "nistp384": the algorithm name and the curve disagree.
        wrongCurve[^72] = (byte)'3';
        wrongCurve[^71] = (byte)'8';
        wrongCurve[^70] = (byte)'4';

        AssertRefused(SftpTestKeys.FromBlob("ssh-ed25519", shortEd25519));
        AssertRefused(SftpTestKeys.FromBlob("ecdsa-sha2-nistp256", compressed));
        AssertRefused(SftpTestKeys.FromBlob("ecdsa-sha2-nistp256", wrongCurve));
    }

    /// <summary>The second field is refused unless it is the canonical padded base64 of its bytes.</summary>
    [Fact]
    public void A_second_field_that_is_not_canonical_base64_is_refused()
    {
        var p256Data = SftpTestKeys.P256.Split(' ')[1];

        // Missing padding: the bytes are the same, the spelling is not.
        Assert.EndsWith("=", p256Data, StringComparison.Ordinal);
        AssertRefused("ecdsa-sha2-nistp256 " + p256Data.TrimEnd('='));

        // Non-zero bits after the last whole byte: a lenient decoder drops them silently.
        AssertRefused("ecdsa-sha2-nistp256 " + p256Data[..^2] + "R=");

        // Characters outside the standard alphabet, including the URL-safe spelling of two of them.
        // Each replacement must change the text, or the case would be refusing nothing.
        var ed25519Data = Good.Split(' ')[1];
        var p384Data = SftpTestKeys.P384.Split(' ')[1];
        Assert.Contains('/', ed25519Data);
        Assert.Contains('+', p384Data);
        AssertRefused("ssh-ed25519 " + ed25519Data.Replace('/', '_'));
        AssertRefused("ecdsa-sha2-nistp384 " + p384Data.Replace('+', '-'));
        AssertRefused("ssh-ed25519 " + ed25519Data[..^1] + "!");
        AssertRefused("ssh-ed25519 !!!!");
    }

    /// <summary>Text one byte over the size limit is refused, as is text over it only in bytes.</summary>
    [Fact]
    public void Text_over_the_size_limit_is_refused_in_bytes_not_characters()
    {
        var overByOne = Good + " " + new string('c', SftpPublicKey.MaxLength - Good.Length);

        // 4100 two-byte characters are fewer than 8192 characters and more than 8192 bytes.
        var overInBytes = Good + " " + new string('é', 4100);

        Assert.Equal(SftpPublicKey.MaxLength + 1, overByOne.Length);
        Assert.True(overInBytes.Length < SftpPublicKey.MaxLength);
        AssertRefused(overByOne);
        AssertRefused(overInBytes);
    }

    /// <summary>A refusal is a validation failure under one code and never carries the text it refused.</summary>
    [Fact]
    public void A_refusal_is_one_validation_code_and_never_carries_the_text()
    {
        var refused = SftpPublicKey.Parse("command=\"secret-marker\" " + Good);

        Assert.False(refused.IsSuccess);
        Assert.Equal("SftpKeyInvalid", refused.Error!.Code);
        Assert.Equal(ErrorType.Validation, refused.Error.Type);
        Assert.DoesNotContain("secret-marker", refused.Error.Code, StringComparison.Ordinal);
    }

    /// <summary>Asserts that <paramref name="text"/> is refused with the key-invalid code.</summary>
    /// <param name="text">The pasted text.</param>
    private static void AssertRefused(string? text)
    {
        var parsed = SftpPublicKey.Parse(text);

        Assert.False(parsed.IsSuccess);
        Assert.Equal("SftpKeyInvalid", parsed.Error!.Code);
    }
}
