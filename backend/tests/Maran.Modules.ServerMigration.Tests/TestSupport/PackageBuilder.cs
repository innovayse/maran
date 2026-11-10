using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Maran.Modules.ServerMigration.Models;
using Maran.Modules.ServerMigration.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Signatures;

namespace Maran.Modules.ServerMigration.Tests.TestSupport;

/// <summary>
/// Builds the head of a package the way the writer does, with a key the test owns, so the reader can be handed
/// a good package and then the same package damaged one way at a time.
/// </summary>
public sealed class PackageBuilder
{
    /// <summary>The databases the manifest lists.</summary>
    private static readonly string[] DatabaseNames = ["alice_db"];

    /// <summary>The key that signs both halves.</summary>
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP384);

    /// <summary>The length of the head of the package <see cref="Build"/> built last; <c>null</c> before the first build.</summary>
    private int? _headLength;

    /// <summary>The artifact bytes the package declares.</summary>
    public byte[] Artifact { get; } = Encoding.UTF8.GetBytes("artifact-bytes");

    /// <summary>The account the package is for.</summary>
    public string Account { get; init; } = "alice";

    /// <summary>The schema the header claims.</summary>
    public int Schema { get; init; } = MigrationPackageReader.SupportedSchema;

    /// <summary>Overrides the digest the header states for the agent's manifest; <c>null</c> states the true one.</summary>
    public string? ManifestDigestOverride { get; init; }

    /// <summary>The uncompressed SEC1 public point of the key.</summary>
    public byte[] PublicKey
    {
        get
        {
            var parameters = _key.ExportParameters(includePrivateParameters: false);
            var point = new byte[EcdsaP384Signature.UncompressedPointBytes];
            point[0] = 0x04;
            parameters.Q.X!.CopyTo(point, 1);
            parameters.Q.Y!.CopyTo(point, 49);

            return point;
        }
    }

    /// <summary>The fingerprint of <see cref="PublicKey"/>, which the operator confirms.</summary>
    public string Fingerprint
    {
        get { return PublicKeyFingerprint.Of(PublicKey); }
    }

    /// <summary>Builds the whole package: the head and then the artifact.</summary>
    /// <returns>The bytes of the package.</returns>
    public byte[] Build()
    {
        var manifest = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(
                new Dictionary<string, object>
                {
                    ["version"] = 1,
                    ["account"] = Account,
                    ["backup_id"] = "5d2f1f0e-2f4e-4e8f-9a31-0123456789ab",
                    ["artifact_bytes"] = Artifact.Length,
                    ["artifact_sha256"] = Convert.ToHexStringLower(SHA256.HashData(Artifact)),
                    ["home_bytes"] = 10,
                    ["databases"] = DatabaseNames,
                    ["created_at_unix"] = 1,
                    ["agent_version"] = "1",
                    ["signer_fingerprint"] = Fingerprint,
                }));
        var header = new MigrationPackageHeader(
            Schema,
            "source.example",
            Fingerprint,
            Account,
            10,
            ManifestDigestOverride ?? Convert.ToHexStringLower(SHA256.HashData(manifest)),
            new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero),
            [new MigrationSection("accounts", 1, "{}")]);
        var headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, JsonSerializerOptions.Web);

        using var stream = new MemoryStream();
        stream.Write(MigrationPackageReader.Magic);
        Field(stream, 2, PublicKey);
        Field(stream, 4, headerBytes);
        Field(stream, 2, Sign(headerBytes));
        Field(stream, 4, manifest);
        Field(stream, 2, Sign(manifest));
        _headLength = (int)stream.Length;
        stream.Write(Artifact);

        return stream.ToArray();
    }

    /// <summary>The length of the head: everything before the first artifact byte of the package <see cref="Build"/> built last.</summary>
    /// <returns>The head's length in bytes.</returns>
    /// <remarks>
    /// Read from the package already built and never from a fresh one: an ECDSA signature is randomised and its DER
    /// encoding is 70 to 72 bytes, so two builds of the same package differ in length by a byte or two.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Nothing has been built yet.</exception>
    public int HeadLength()
    {
        return _headLength ?? throw new InvalidOperationException("Build the package before asking for its head length.");
    }

    /// <summary>Signs bytes with the key.</summary>
    /// <param name="data">The bytes to sign.</param>
    /// <returns>The ASN.1 DER signature.</returns>
    private byte[] Sign(byte[] data)
    {
        return _key.SignData(data, HashAlgorithmName.SHA384, DSASignatureFormat.Rfc3279DerSequence);
    }

    /// <summary>Writes one length-prefixed field.</summary>
    /// <param name="stream">Where it goes.</param>
    /// <param name="prefixBytes">2 for a 16-bit length, 4 for a 32-bit one.</param>
    /// <param name="field">The field's bytes.</param>
    private static void Field(Stream stream, int prefixBytes, byte[] field)
    {
        var prefix = new byte[prefixBytes];

        if (prefixBytes == 2)
        {
            BinaryPrimitives.WriteUInt16BigEndian(prefix, (ushort)field.Length);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(prefix, (uint)field.Length);
        }

        stream.Write(prefix);
        stream.Write(field);
    }
}
