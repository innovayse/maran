using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Maran.Host.IntegrationTests.Modules;

/// <summary>
/// The package format the fake loader understands: a module's identity, an opaque body, and a seal over
/// all of it made with a named key. It stands in for the signed package of the real format so that the
/// loader contract can be exercised end to end without that format or its keys; the seal plays the part
/// of the signature, and the three ways it can be wrong (absent, made with another key, broken by an
/// edit) are the three refusals the contract tests plant.
/// </summary>
/// <param name="Id">The module's id.</param>
/// <param name="Version">The module's version.</param>
/// <param name="Body">The package's payload; any edit after sealing breaks the seal.</param>
/// <param name="Seal">The hex seal over id, version and body, or empty for an unsealed package.</param>
public sealed record FakePackage(string Id, string Version, string Body, string Seal)
{
    /// <summary>The key the fake loader trusts. A test key, never a real one.</summary>
    public const string TrustedKey = "fake-trusted-key";

    /// <summary>A key the fake loader does not trust.</summary>
    public const string OtherKey = "fake-other-key";

    /// <summary>Builds a package sealed with a key.</summary>
    /// <param name="id">The module's id.</param>
    /// <param name="version">The module's version.</param>
    /// <param name="key">The key to seal with; <see cref="TrustedKey"/> for a package the loader accepts.</param>
    /// <param name="body">The payload.</param>
    /// <returns>The sealed package.</returns>
    public static FakePackage Sealed(string id, string version, string key = TrustedKey, string body = "payload")
    {
        return new FakePackage(id, version, body, SealOf(key, id, version, body));
    }

    /// <summary>Reads a package from its bytes.</summary>
    /// <param name="bytes">The bytes a source supplied.</param>
    /// <returns>The package, or null when the bytes are not one.</returns>
    public static FakePackage? TryRead(byte[] bytes)
    {
        try
        {
            var read = JsonSerializer.Deserialize<FakePackage>(bytes);

            // A document that parses but lacks a member is not a package: the record's members are
            // non-nullable by declaration and null by what a stranger's bytes can make them.
            return read is { Id: not null, Version: not null, Body: not null, Seal: not null } ? read : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Computes the seal a key makes over a module.</summary>
    /// <param name="key">The sealing key.</param>
    /// <param name="id">The module's id.</param>
    /// <param name="version">The module's version.</param>
    /// <param name="body">The payload.</param>
    /// <returns>The seal as lowercase hex.</returns>
    public static string SealOf(string key, string id, string version, string body)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{key}|{id}|{version}|{body}")));
    }

    /// <summary>Whether the seal is the one the trusted key makes over this package.</summary>
    /// <returns>True when the package is exactly what the trusted key sealed.</returns>
    public bool IsSealedByTrustedKey()
    {
        return Seal.Length > 0
            && string.Equals(Seal, SealOf(TrustedKey, Id, Version, Body), StringComparison.Ordinal);
    }

    /// <summary>Writes the package as the bytes a source supplies.</summary>
    /// <returns>The bytes.</returns>
    public byte[] ToBytes()
    {
        return JsonSerializer.SerializeToUtf8Bytes(this);
    }
}
