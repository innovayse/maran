using Maran.Modules.ServerMigration.Services;
using Maran.Modules.ServerMigration.Tests.TestSupport;

namespace Maran.Modules.ServerMigration.Tests.Services;

/// <summary>
/// The strict reader of an untrusted package (issue #127). Every refusal is paired with the acceptance of the
/// same package undamaged, so a reader mutated to refuse everything fails here too.
/// </summary>
public sealed class MigrationPackageReaderTests
{
    /// <summary>Reads a package's head.</summary>
    /// <param name="bytes">The package.</param>
    /// <param name="fingerprint">The fingerprint the operator confirmed.</param>
    /// <returns>The result and the stream, left at the artifact.</returns>
    private static async Task<(Maran.SharedKernel.Results.Result<Maran.Modules.ServerMigration.Models.VerifiedPackage> Result, MemoryStream Stream)> ReadAsync(
        byte[] bytes,
        string fingerprint)
    {
        var stream = new MemoryStream(bytes);

        return (await MigrationPackageReader.ReadAsync(stream, fingerprint, CancellationToken.None), stream);
    }

    /// <summary>The inverse control: an undamaged package with the right fingerprint is accepted and the stream is left at the artifact.</summary>
    [Fact]
    public async Task A_valid_package_is_accepted_and_the_stream_is_left_at_the_artifact()
    {
        var builder = new PackageBuilder();
        var bytes = builder.Build();

        var (result, stream) = await ReadAsync(bytes, builder.Fingerprint);

        Assert.True(result.IsSuccess);
        Assert.Equal("alice", result.Value!.Header.AccountName);
        Assert.Equal(builder.Artifact.Length, result.Value.Manifest.ArtifactBytes);
        Assert.Equal(builder.HeadLength(), stream.Position);
    }

    /// <summary>A fingerprint other than the one the operator confirmed refuses the package before its header is read.</summary>
    [Fact]
    public async Task A_package_signed_by_a_key_the_operator_did_not_confirm_is_refused()
    {
        var builder = new PackageBuilder();

        var (result, _) = await ReadAsync(builder.Build(), new string('0', 64));

        Assert.Equal("MigrationFingerprintMismatch", result.Error!.Code);
    }

    /// <summary>One flipped bit anywhere in the signed header refuses the package.</summary>
    [Fact]
    public async Task One_flipped_bit_in_the_header_refuses_the_package()
    {
        var builder = new PackageBuilder();
        var bytes = builder.Build();
        bytes[10 + 2 + 97 + 4 + 5] ^= 0x01;

        var (result, _) = await ReadAsync(bytes, builder.Fingerprint);

        Assert.False(result.IsSuccess);
    }

    /// <summary>A header whose schema this reader does not know is refused, not guessed at.</summary>
    [Fact]
    public async Task An_unknown_schema_is_refused()
    {
        var builder = new PackageBuilder { Schema = 99 };

        var (result, _) = await ReadAsync(builder.Build(), builder.Fingerprint);

        Assert.Equal("MigrationPackageSchemaUnknown", result.Error!.Code);
    }

    /// <summary>A manifest that does not match the digest the signed header states is refused.</summary>
    [Fact]
    public async Task A_manifest_the_header_does_not_bind_is_refused()
    {
        var builder = new PackageBuilder { ManifestDigestOverride = new string('f', 64) };

        var (result, _) = await ReadAsync(builder.Build(), builder.Fingerprint);

        Assert.Equal("MigrationPackageMalformed", result.Error!.Code);
    }

    /// <summary>A stream that ends inside the head is refused.</summary>
    [Fact]
    public async Task A_truncated_head_is_refused()
    {
        var builder = new PackageBuilder();
        var bytes = builder.Build().AsSpan(0, builder.HeadLength() - 20).ToArray();

        var (result, _) = await ReadAsync(bytes, builder.Fingerprint);

        Assert.Equal("MigrationPackageMalformed", result.Error!.Code);
    }

    /// <summary>A stream that does not start with the magic is not a package.</summary>
    [Fact]
    public async Task Bytes_that_are_not_a_package_are_refused()
    {
        var (result, _) = await ReadAsync(new byte[200], new string('0', 64));

        Assert.Equal("MigrationPackageMalformed", result.Error!.Code);
    }

    /// <summary>Bytes after the signed artifact length are found, so the package is refused by the caller.</summary>
    [Fact]
    public async Task Trailing_bytes_after_the_artifact_are_detected_and_a_clean_end_is_not()
    {
        var builder = new PackageBuilder();
        var clean = builder.Build();
        var padded = clean.Concat(new byte[] { 1 }).ToArray();

        var (_, cleanStream) = await ReadAsync(clean, builder.Fingerprint);
        var (_, paddedStream) = await ReadAsync(padded, builder.Fingerprint);
        var artifactOfClean = new BoundedReadStream(cleanStream, builder.Artifact.Length);
        var artifactOfPadded = new BoundedReadStream(paddedStream, builder.Artifact.Length);
        await artifactOfClean.CopyToAsync(Stream.Null);
        await artifactOfPadded.CopyToAsync(Stream.Null);

        Assert.False(await MigrationPackageReader.HasTrailingBytesAsync(cleanStream, CancellationToken.None));
        Assert.True(await MigrationPackageReader.HasTrailingBytesAsync(paddedStream, CancellationToken.None));
    }
}
