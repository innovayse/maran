using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Modules.Licensing.Services;
using Maran.Modules.Licensing.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Covers <see cref="LeaseEnvelopeReader"/>, the one parser of the lease format that the stored file and the
/// licence service's answer are both judged by: blank text is no lease, a lease signed by the pinned key is read
/// with its signed bytes, and anything else is refused with its reason and no bytes.
/// </summary>
public sealed class LeaseEnvelopeReaderTests
{
    /// <summary>When the lease in these tests was issued.</summary>
    private static readonly DateTimeOffset Issued = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A reader that trusts the given key.</summary>
    /// <param name="key">The lease key it trusts.</param>
    /// <returns>The reader.</returns>
    private static LeaseEnvelopeReader Trusting(LeaseVerificationKey key)
    {
        return new LeaseEnvelopeReader(new Ed25519LeaseSignatureVerifier(key));
    }

    /// <summary>A lease signed by the pinned key is read, and its signed bytes come with it.</summary>
    [Fact]
    public void A_lease_signed_by_the_pinned_key_is_read_with_its_signed_bytes()
    {
        var reading = Trusting(TestKeys.TrustedLeaseKey).Read(
            LeaseEnvelopeBuilder.Build(TestKeys.LeasePrivateKeyHex, "lic-001", Issued, Issued.AddDays(14), "abc"));

        var present = Assert.IsType<LeaseReading.Present>(reading.Reading);
        Assert.Equal(new LicenceLease("lic-001", Issued, Issued.AddDays(14), "abc"), present.Lease);
        Assert.NotNull(reading.Verified);
        Assert.Equal(64, reading.Verified.Signature.Length);
    }

    /// <summary>A lease signed by another key is refused with no signed bytes; the pinned key is the control above.</summary>
    [Fact]
    public void A_lease_signed_by_another_key_is_refused_and_carries_no_signed_bytes()
    {
        var reading = Trusting(TestKeys.TrustedLeaseKey).Read(
            LeaseEnvelopeBuilder.Build(TestKeys.OtherPrivateKeyHex, "lic-001", Issued, Issued.AddDays(14)));

        Assert.Equal(new LeaseReading.Refused(LeaseRefusalReason.SignatureInvalid), reading.Reading);
        Assert.Null(reading.Verified);
    }

    /// <summary>Blank text is no lease, not a refusal, so an empty answer is not read as a forgery.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    public void Blank_text_is_absent(string text)
    {
        var reading = Trusting(TestKeys.TrustedLeaseKey).Read(text);

        Assert.IsType<LeaseReading.Absent>(reading.Reading);
        Assert.Null(reading.Verified);
    }

    /// <summary>A build with no lease key refuses even a correctly signed lease, for that reason.</summary>
    [Fact]
    public void A_build_with_no_lease_key_refuses_every_lease_for_that_reason()
    {
        var reading = Trusting(LeaseVerificationKey.None).Read(
            LeaseEnvelopeBuilder.Build(TestKeys.LeasePrivateKeyHex, "lic-001", Issued, Issued.AddDays(14)));

        Assert.Equal(new LeaseReading.Refused(LeaseRefusalReason.NoVerificationKey), reading.Reading);
    }

    /// <summary>Text that is not an envelope, and an envelope that is not a lease, are malformed.</summary>
    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"payload\":{}}")]
    [InlineData("{\"payload\":{},\"signature\":5}")]
    public void Text_that_is_not_an_envelope_is_malformed(string text)
    {
        var reading = Trusting(TestKeys.TrustedLeaseKey).Read(text);

        Assert.Equal(new LeaseReading.Refused(LeaseRefusalReason.Malformed), reading.Reading);
    }
}
