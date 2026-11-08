using System.IO.Compression;
using System.Security.Cryptography;
using Maran.Packaging.Constants;
using Maran.Packaging.Models;
using Maran.Packaging.Services;
using Maran.Sdk.Contracts;

namespace Maran.Host.Tests.Cli.Fixtures;

/// <summary>
/// A temporary folder that writes small module packages and the public keys that verify them, signed with
/// a key the test generates. Nothing here is a real key and nothing is committed: the keys exist for one
/// test and die with the folder.
/// </summary>
internal sealed class ModulePackageWorkspace : IDisposable
{
    /// <summary>The assembly name inside every package this workspace writes.</summary>
    private const string AssemblyName = "Tests.dll";

    /// <summary>The folder everything is written into.</summary>
    private readonly string _folder;

    /// <summary>Creates the workspace.</summary>
    public ModulePackageWorkspace()
    {
        _folder = Directory.CreateTempSubdirectory("maran-ipm-cli-").FullName;
    }

    /// <summary>Generates a signing key of the scheme every package is signed with.</summary>
    /// <returns>A new ECDSA P-384 key pair.</returns>
    public static ECDsa NewSigningKey()
    {
        return ECDsa.Create(ECCurve.NamedCurves.nistP384);
    }

    /// <summary>Writes the public half of a key as a PEM file.</summary>
    /// <param name="key">The key whose public half is written.</param>
    /// <returns>The file's path.</returns>
    public string WritePublicKey(ECDsa key)
    {
        return WriteText($"{Guid.NewGuid():N}.pub", key.ExportSubjectPublicKeyInfoPem());
    }

    /// <summary>Writes a file of text.</summary>
    /// <param name="name">The file name.</param>
    /// <param name="text">The text.</param>
    /// <returns>The file's path.</returns>
    public string WriteText(string name, string text)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>Writes a package whose manifest pins one assembly and whose signature covers the manifest.</summary>
    /// <param name="signer">The key that signs <c>ipm.json</c>.</param>
    /// <param name="pinned">The bytes the manifest's hash and size are computed from.</param>
    /// <param name="stored">The bytes actually stored in the archive; the same as <paramref name="pinned"/> unless a test alters them.</param>
    /// <returns>The package's path.</returns>
    public string WritePackage(ECDsa signer, byte[] pinned, byte[] stored)
    {
        var manifest = new IpmManifest(
            IpmLayout.CurrentFormatVersion,
            "tests",
            "TestsModuleDisplayName",
            "1.0.0",
            LicenceTier.AddOn,
            ModuleAudience.AdministratorOnly,
            "1.0.0",
            null,
            null,
            null,
            [],
            [],
            [],
            [],
            [],
            null,
            AssemblyName,
            null,
            null,
            [new IpmFileEntry($"lib/{AssemblyName}", Convert.ToHexStringLower(SHA384.HashData(pinned)), pinned.Length)]);
        var manifestBytes = IpmManifestCodec.Serialize(manifest);
        var signature = signer.SignData(manifestBytes, HashAlgorithmName.SHA384, DSASignatureFormat.Rfc3279DerSequence);

        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.ipm");
        using (var file = File.Create(path))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            AddEntry(zip, "ipm.json", manifestBytes);
            AddEntry(zip, "ipm.sig", signature);
            AddEntry(zip, $"lib/{AssemblyName}", stored);
        }

        return path;
    }

    /// <summary>Removes the folder and everything in it.</summary>
    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }

    /// <summary>Adds one entry to an archive.</summary>
    /// <param name="zip">The archive being written.</param>
    /// <param name="name">The entry name.</param>
    /// <param name="bytes">The entry's content.</param>
    private static void AddEntry(ZipArchive zip, string name, byte[] bytes)
    {
        using var stream = zip.CreateEntry(name).Open();
        stream.Write(bytes);
    }
}
