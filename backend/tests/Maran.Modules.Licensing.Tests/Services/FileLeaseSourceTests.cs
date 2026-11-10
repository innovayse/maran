using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Modules.Licensing.Options;
using Maran.Modules.Licensing.Services;
using Maran.Modules.Licensing.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Covers <see cref="FileLeaseSource"/>: it hands over only a lease whose signature held, tells "no
/// lease" from "a lease that cannot be trusted", is bounded in size, and never throws.
/// </summary>
public sealed class FileLeaseSourceTests : IDisposable
{
    /// <summary>When the lease in these tests was issued.</summary>
    private static readonly DateTimeOffset Issued = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A directory of its own, removed afterwards.</summary>
    private readonly string _directory =
        Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "lease-source-tests-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>The lease file the source reads.</summary>
    private string LeasePath
    {
        get { return Path.Combine(_directory, "lease.json"); }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    /// <summary>Builds a source over the lease file, trusting the given key.</summary>
    /// <param name="key">The lease key the source trusts.</param>
    /// <returns>The source.</returns>
    private FileLeaseSource SourceTrusting(LeaseVerificationKey key)
    {
        return new FileLeaseSource(
            new Microsoft.Extensions.Options.OptionsWrapper<LeaseStorageOptions>(
                new LeaseStorageOptions { LeaseFilePath = LeasePath }),
            new Ed25519LeaseSignatureVerifier(key));
    }

    /// <summary>A properly signed lease is handed over with its fields.</summary>
    [Fact]
    public async Task A_lease_signed_by_the_pinned_key_is_handed_over()
    {
        await File.WriteAllTextAsync(
            LeasePath,
            LeaseEnvelopeBuilder.Build(TestKeys.LeasePrivateKeyHex, "lic-001", Issued, Issued.AddDays(14), "abc"));

        var reading = await SourceTrusting(TestKeys.TrustedLeaseKey).ReadAsync(CancellationToken.None);

        var present = Assert.IsType<LeaseReading.Present>(reading);
        Assert.Equal(new LicenceLease("lic-001", Issued, Issued.AddDays(14), "abc"), present.Lease);
    }

    /// <summary>The signed bytes of a verified lease are handed over exactly as signed; a lease that cannot be trusted has none.</summary>
    [Fact]
    public async Task The_signed_bytes_are_handed_over_only_for_a_lease_that_verified()
    {
        await File.WriteAllTextAsync(
            LeasePath,
            LeaseEnvelopeBuilder.Build(TestKeys.LeasePrivateKeyHex, "lic-001", Issued, Issued.AddDays(14), "abc"));
        var source = SourceTrusting(TestKeys.TrustedLeaseKey);

        var signed = await source.ReadSignedAsync(CancellationToken.None);

        Assert.NotNull(signed);
        Assert.Contains("\"licence\":\"lic-001\"", System.Text.Encoding.UTF8.GetString(signed.PayloadBytes), StringComparison.Ordinal);
        Assert.Equal(64, signed.Signature.Length);

        await File.WriteAllTextAsync(
            LeasePath,
            LeaseEnvelopeBuilder.Build(TestKeys.OtherPrivateKeyHex, "lic-001", Issued, Issued.AddDays(14)));

        Assert.Null(await source.ReadSignedAsync(CancellationToken.None));

        File.Delete(LeasePath);

        Assert.Null(await source.ReadSignedAsync(CancellationToken.None));
    }

    /// <summary>No file, and an empty file, are "no lease", not a refusal.</summary>
    [Fact]
    public async Task A_missing_or_empty_file_is_absent_not_refused()
    {
        var source = SourceTrusting(TestKeys.TrustedLeaseKey);

        Assert.IsType<LeaseReading.Absent>(await source.ReadAsync(CancellationToken.None));

        await File.WriteAllTextAsync(LeasePath, "  \n");

        Assert.IsType<LeaseReading.Absent>(await source.ReadAsync(CancellationToken.None));
    }

    /// <summary>A lease signed by any other key is refused as a bad signature; the right key is the control.</summary>
    [Fact]
    public async Task A_lease_signed_by_another_key_is_refused_as_a_bad_signature()
    {
        await File.WriteAllTextAsync(
            LeasePath,
            LeaseEnvelopeBuilder.Build(TestKeys.OtherPrivateKeyHex, "lic-001", Issued, Issued.AddDays(14)));

        var reading = await SourceTrusting(TestKeys.TrustedLeaseKey).ReadAsync(CancellationToken.None);

        Assert.Equal(
            new LeaseReading.Refused(LeaseRefusalReason.SignatureInvalid),
            reading);
    }

    /// <summary>The licence key does not verify a lease: a lease signed by the LICENCE seed is refused (decision D3).</summary>
    [Fact]
    public async Task A_lease_signed_with_the_licence_seed_is_refused_because_the_keys_are_separate()
    {
        await File.WriteAllTextAsync(
            LeasePath,
            LeaseEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, "lic-001", Issued, Issued.AddDays(14)));

        var reading = await SourceTrusting(TestKeys.TrustedLeaseKey).ReadAsync(CancellationToken.None);

        Assert.Equal(new LeaseReading.Refused(LeaseRefusalReason.SignatureInvalid), reading);
    }

    /// <summary>A build with no lease key refuses every lease and says why, even a correctly signed one.</summary>
    [Fact]
    public async Task A_build_with_no_lease_key_refuses_every_lease_for_that_reason()
    {
        await File.WriteAllTextAsync(
            LeasePath,
            LeaseEnvelopeBuilder.Build(TestKeys.LeasePrivateKeyHex, "lic-001", Issued, Issued.AddDays(14)));

        var reading = await SourceTrusting(LeaseVerificationKey.None).ReadAsync(CancellationToken.None);

        Assert.Equal(new LeaseReading.Refused(LeaseRefusalReason.NoVerificationKey), reading);
    }

    /// <summary>A validly signed payload that is not a lease, or is mistyped, or ends before it starts, is malformed.</summary>
    [Theory]
    [InlineData("{\"kind\":\"licence\",\"licence\":\"lic-001\",\"issued\":\"2026-03-01T00:00:00Z\",\"validUntil\":\"2026-03-15T00:00:00Z\"}")]
    [InlineData("{\"kind\":\"lease\",\"licence\":\"\",\"issued\":\"2026-03-01T00:00:00Z\",\"validUntil\":\"2026-03-15T00:00:00Z\"}")]
    [InlineData("{\"kind\":\"lease\",\"licence\":\"lic-001\",\"issued\":\"not a date\",\"validUntil\":\"2026-03-15T00:00:00Z\"}")]
    [InlineData("{\"kind\":\"lease\",\"licence\":\"lic-001\",\"issued\":\"2026-03-15T00:00:00Z\",\"validUntil\":\"2026-03-01T00:00:00Z\"}")]
    [InlineData("{\"kind\":\"lease\",\"licence\":\"lic-001\",\"issued\":\"2026-03-01T00:00:00Z\",\"validUntil\":\"2026-03-15T00:00:00Z\",\"server\":7}")]
    public async Task A_signed_payload_that_is_not_a_well_formed_lease_is_malformed(string payload)
    {
        await File.WriteAllTextAsync(LeasePath, LeaseEnvelopeBuilder.BuildRaw(TestKeys.LeasePrivateKeyHex, payload));

        var reading = await SourceTrusting(TestKeys.TrustedLeaseKey).ReadAsync(CancellationToken.None);

        Assert.Equal(new LeaseReading.Refused(LeaseRefusalReason.Malformed), reading);
    }

    /// <summary>Text that is not even an envelope is malformed, and nothing throws.</summary>
    [Fact]
    public async Task Text_that_is_not_an_envelope_is_malformed_and_nothing_throws()
    {
        await File.WriteAllTextAsync(LeasePath, "this is not json");

        var reading = await SourceTrusting(TestKeys.TrustedLeaseKey).ReadAsync(CancellationToken.None);

        Assert.Equal(new LeaseReading.Refused(LeaseRefusalReason.Malformed), reading);
    }

    /// <summary>A file larger than the bound is refused unread; one inside the bound is read.</summary>
    [Fact]
    public async Task A_file_larger_than_the_bound_is_refused_without_being_read()
    {
        await File.WriteAllTextAsync(LeasePath, new string(' ', LeaseStorageOptions.MaxLeaseFileBytes + 1));

        var reading = await SourceTrusting(TestKeys.TrustedLeaseKey).ReadAsync(CancellationToken.None);

        Assert.Equal(new LeaseReading.Refused(LeaseRefusalReason.Malformed), reading);
    }
}
