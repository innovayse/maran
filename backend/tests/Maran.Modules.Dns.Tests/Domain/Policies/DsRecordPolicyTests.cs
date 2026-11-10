using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Policies;

namespace Maran.Modules.Dns.Tests.Domain.Policies;

/// <summary>
/// The DS record the owner publishes at the registrar. Every expected value below was derived independently of
/// the code under test, and how is stated at each one, because a digest the production code computed and a test
/// copied from it would agree with any bug the code has.
/// </summary>
public sealed class DsRecordPolicyTests
{
    /// <summary>
    /// The DNSKEY public key of RFC 4034 section 5.4 (<c>dskey.example.com</c>, flags 256, algorithm 5, key id
    /// 60485), the key RFC 4509 section 2.3 publishes a SHA-256 DS record for.
    /// </summary>
    private const string Rfc4034Key =
        "AQOeiiR0GOMYkDshWoSKz9XzfwJr1AYtsmx3TGkJaNXVbfi/2pHm822aJ5iI9BMzNXxeYCmZDRD99WYwYqUSdjMmmAphXdvxegXd/M5+X7OrzKBaMbCVdFLUUh6DhweJBjEVv5f2wwjM9XzcnOf+EPbtG9DMBmADjFDc2w/rljwvFw==";

    /// <summary>
    /// The DS digest RFC 4509 section 2.3 prints for that key. Reproduced here by python3 <c>hashlib.sha256</c>
    /// over a wire name built by hand in the script, which also printed key id 60485.
    /// </summary>
    private const string Rfc4509Digest = "D4B7D520E7BB5F0F67674A0CCEB1E3E0614B93C4F9E99B8383F6A1E4469DA50A";

    /// <summary>
    /// The ECDSA P-256 DNSKEY of RFC 6605 section 6.1 (<c>example.net</c>, flags 257, algorithm 13, key id
    /// 55648): the x and y coordinates, 64 bytes.
    /// </summary>
    private const string Rfc6605Key =
        "GojIhhXUN/u4v54ZQqGSnyhWJwaubCvTmeexv7bR6edbkrSqQpF64cYbcB7wNcP+e+MAnLr+Wi9xMWyQLc8NAA==";

    /// <summary>
    /// The SHA-256 DS digest of that key at <c>example.net</c>. RFC 6605 prints no DS record, so it was computed
    /// two ways over the same bytes (07 'example' 03 'net' 00, then flags 0x0101, protocol 3, algorithm 13, the
    /// key): python3 <c>hashlib.sha256</c>, and <c>openssl dgst -sha256</c> over a file holding them. The two
    /// agreed, and the python script also printed key id 55648, the RFC's own number.
    /// </summary>
    private const string Rfc6605Digest = "B4C8C1FE2E7477127B27115656AD6256F424625BF5C1E2770CE6D6E37DF61D17";

    /// <summary>The key of RFC 6605 as bytes.</summary>
    private static readonly byte[] P256Key = Convert.FromBase64String(Rfc6605Key);

    /// <summary>The example key of RFC 4034 as bytes.</summary>
    private static readonly byte[] RsaKey = Convert.FromBase64String(Rfc4034Key);

    /// <summary>The RFC 4034 / 4509 key at the RFC's owner name gives the published key tag and the published digest.</summary>
    [Fact]
    public void The_rfc_4034_example_key_gives_the_published_tag_and_the_published_sha256_digest()
    {
        var ds = DsRecordPolicy.Compute("dskey.example.com", 256, 5, RsaKey);

        Assert.Equal(60485, ds.KeyTag);
        Assert.Equal(5, ds.Algorithm);
        Assert.Equal(2, ds.DigestType);
        Assert.Equal(Rfc4509Digest, ds.Digest);
    }

    /// <summary>The ECDSA P-256 key of RFC 6605 gives its published key id and the independently computed digest.</summary>
    [Fact]
    public void The_rfc_6605_key_gives_its_key_id_and_the_independently_computed_digest()
    {
        var ds = DsRecordPolicy.Compute("example.net", 257, 13, P256Key);

        Assert.Equal(55648, ds.KeyTag);
        Assert.Equal(13, ds.Algorithm);
        Assert.Equal(Rfc6605Digest, ds.Digest);
    }

    /// <summary>The digest is upper-case hexadecimal of 32 bytes.</summary>
    [Fact]
    public void The_digest_is_sixty_four_upper_case_hexadecimal_characters()
    {
        var digest = DsRecordPolicy.Compute("example.net", 257, 13, P256Key).Digest;

        Assert.Equal(64, digest.Length);
        Assert.Equal(digest.ToUpperInvariant(), digest);
        Assert.Matches("^[0-9A-F]{64}$", digest);
    }

    /// <summary>Inverse control: the owner name is canonicalised, so case and a trailing dot do not change the digest.</summary>
    [Theory]
    [InlineData("EXAMPLE.NET")]
    [InlineData("example.net.")]
    [InlineData("Example.Net.")]
    public void The_owner_name_is_canonical_so_case_and_a_trailing_dot_do_not_change_the_digest(string spelling)
    {
        Assert.Equal(Rfc6605Digest, DsRecordPolicy.Compute(spelling, 257, 13, P256Key).Digest);
    }

    /// <summary>Planted difference: another owner name, another flag or another key bit each gives another digest.</summary>
    [Fact]
    public void A_different_owner_flag_or_key_gives_a_different_digest()
    {
        var flipped = (byte[])P256Key.Clone();
        flipped[10] ^= 0x01;

        Assert.NotEqual(Rfc6605Digest, DsRecordPolicy.Compute("example.org", 257, 13, P256Key).Digest);
        Assert.NotEqual(Rfc6605Digest, DsRecordPolicy.Compute("example.net", 256, 13, P256Key).Digest);
        Assert.NotEqual(Rfc6605Digest, DsRecordPolicy.Compute("example.net", 257, 13, flipped).Digest);
    }

    /// <summary>The record for a zone's stored key is the record of that key at that zone's name.</summary>
    [Fact]
    public void The_record_for_a_zones_stored_key_is_the_record_of_that_key_at_the_zones_name()
    {
        var zone = new Zone(Guid.NewGuid(), Guid.NewGuid(), "example.net", new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero));
        var material = DnssecKeyPolicy.FromParts(P256Key[..32], P256Key[32..], new byte[32]);
        zone.EnableSigning(Guid.NewGuid(), material, zone.CreatedAt);

        var ds = DsRecordPolicy.For(zone.Name, zone.SigningKeys.Single());

        Assert.Equal(Rfc6605Digest, ds.Digest);
        Assert.Equal(55648, ds.KeyTag);
    }

    /// <summary>A name that cannot be written in wire form is refused rather than hashed.</summary>
    [Theory]
    [InlineData("a..net")]
    [InlineData("")]
    [InlineData("exämple.net")]
    public void A_name_that_has_no_wire_form_is_refused(string name)
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return DsRecordPolicy.Compute(name, 257, 13, P256Key);
        });
    }

    /// <summary>A label of 63 bytes is the longest that is hashed; 64 is refused.</summary>
    [Fact]
    public void A_label_of_sixty_three_bytes_is_accepted_and_one_of_sixty_four_is_refused()
    {
        var longest = new string('a', 63) + ".net";
        var tooLong = new string('a', 64) + ".net";

        Assert.Equal(64, DsRecordPolicy.Compute(longest, 257, 13, P256Key).Digest.Length);
        Assert.Throws<ArgumentException>(() =>
        {
            return DsRecordPolicy.Compute(tooLong, 257, 13, P256Key);
        });
    }
}
