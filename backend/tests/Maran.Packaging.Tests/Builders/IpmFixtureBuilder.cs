using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Maran.Packaging.Models;
using Maran.Packaging.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Navigation;

namespace Maran.Packaging.Tests.Builders;

/// <summary>
/// Builds <c>.ipm</c> packages in memory, signed with a key the test passes (always a throwaway
/// generated for the run; no key is committed anywhere). A builder starts as a package that every
/// check accepts, and each method plants exactly ONE defect, so a test pairs the defective package
/// with the good one and a verifier changed to refuse everything cannot pass.
/// </summary>
public sealed class IpmFixtureBuilder
{
    /// <summary>The name of the assembly the good package carries.</summary>
    public const string AssemblyName = "Hello.dll";

    /// <summary>The name of the interface bundle's entry file.</summary>
    public const string SpaEntryName = "spa/index.js";

    /// <summary>The name of the fallback translation.</summary>
    public const string EnglishLocaleName = "spa/locales/en.json";

    /// <summary>The key the package is signed with unless a test says otherwise.</summary>
    private readonly ECDsa _key;

    /// <summary>The payload files, in the order they are written.</summary>
    private readonly List<IpmPayloadFile> _payload =
    [
        new("lib/" + AssemblyName, Encoding.UTF8.GetBytes("assembly bytes"), Pinned: true, Stored: true),
        new(SpaEntryName, Encoding.UTF8.GetBytes("console.log('hello');"), Pinned: true, Stored: true),
        new(EnglishLocaleName, Encoding.UTF8.GetBytes("{\"hello\":\"Hello\"}"), Pinned: true, Stored: true),
    ];

    /// <summary>Entries written as they are, with no pin and no check by the builder.</summary>
    private readonly List<IpmRawEntry> _raw = [];

    /// <summary>How the manifest is changed after its pins are computed.</summary>
    private Func<IpmManifest, IpmManifest> _edit = manifest =>
    {
        return manifest;
    };

    /// <summary>The key that signs, when it is not <see cref="_key"/>.</summary>
    private ECDsa? _signer;

    /// <summary>True when the signature entry is left out.</summary>
    private bool _unsigned;

    /// <summary>True when one byte of the manifest is changed after it is signed.</summary>
    private bool _tamperManifest;

    /// <summary>The encoding entry names are written in.</summary>
    private Encoding _nameEncoding = new UTF8Encoding(false);

    /// <summary>Starts a good package.</summary>
    /// <param name="key">The throwaway key that signs it.</param>
    public IpmFixtureBuilder(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _key = key;
    }

    /// <summary>Makes a fresh throwaway P-384 key, as a run of the tests needs.</summary>
    /// <returns>A new key; the caller disposes it.</returns>
    public static ECDsa NewKey()
    {
        return ECDsa.Create(ECCurve.NamedCurves.nistP384);
    }

    /// <summary>Adds a payload file that the manifest pins and the archive stores.</summary>
    /// <param name="name">The entry name.</param>
    /// <param name="bytes">Its content.</param>
    /// <returns>This builder.</returns>
    public IpmFixtureBuilder WithPayload(string name, byte[] bytes)
    {
        _payload.Add(new IpmPayloadFile(name, bytes, Pinned: true, Stored: true));
        return this;
    }

    /// <summary>Adds a file the archive stores but the manifest does not list.</summary>
    /// <param name="name">The entry name.</param>
    /// <param name="bytes">Its content.</param>
    /// <returns>This builder.</returns>
    public IpmFixtureBuilder WithUnlistedFile(string name, byte[] bytes)
    {
        _payload.Add(new IpmPayloadFile(name, bytes, Pinned: false, Stored: true));
        return this;
    }

    /// <summary>Drops a file from the archive while the manifest still lists it.</summary>
    /// <param name="name">The entry name.</param>
    /// <returns>This builder.</returns>
    public IpmFixtureBuilder WithoutStoredFile(string name)
    {
        Replace(name, file =>
        {
            return file with { Stored = false };
        });
        return this;
    }

    /// <summary>Changes a file's stored bytes while the manifest still pins the original.</summary>
    /// <param name="name">The entry name.</param>
    /// <param name="bytes">The bytes actually stored.</param>
    /// <returns>This builder.</returns>
    public IpmFixtureBuilder WithTamperedFile(string name, byte[] bytes)
    {
        Replace(name, file =>
        {
            return file with { StoredBytes = bytes };
        });
        return this;
    }

    /// <summary>Writes an entry exactly as given, with optional Unix external attributes (a symbolic link, say).</summary>
    /// <param name="name">The entry name, written verbatim.</param>
    /// <param name="bytes">Its content.</param>
    /// <param name="externalAttributes">The ZIP external attributes, or null for the writer's default.</param>
    /// <returns>This builder.</returns>
    public IpmFixtureBuilder WithRawEntry(string name, byte[] bytes, int? externalAttributes = null)
    {
        _raw.Add(new IpmRawEntry(name, bytes, externalAttributes));
        return this;
    }

    /// <summary>Changes the manifest after its pins are computed, before it is written and signed.</summary>
    /// <param name="edit">The change.</param>
    /// <returns>This builder.</returns>
    public IpmFixtureBuilder WithManifest(Func<IpmManifest, IpmManifest> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        _edit = edit;
        return this;
    }

    /// <summary>Signs with a different key than the one the verifier trusts.</summary>
    /// <param name="signer">The other key.</param>
    /// <returns>This builder.</returns>
    public IpmFixtureBuilder SignedBy(ECDsa signer)
    {
        _signer = signer;
        return this;
    }

    /// <summary>Leaves the signature entry out.</summary>
    /// <returns>This builder.</returns>
    public IpmFixtureBuilder WithoutSignature()
    {
        _unsigned = true;
        return this;
    }

    /// <summary>Flips one byte of the manifest after it is signed.</summary>
    /// <returns>This builder.</returns>
    public IpmFixtureBuilder WithTamperedManifestByte()
    {
        _tamperManifest = true;
        return this;
    }

    /// <summary>Writes entry names in another encoding, so a non-UTF-8 name can be planted.</summary>
    /// <param name="encoding">The encoding.</param>
    /// <returns>This builder.</returns>
    public IpmFixtureBuilder WithNameEncoding(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        _nameEncoding = encoding;
        return this;
    }

    /// <summary>The manifest of the package as built, after pins and edits.</summary>
    /// <returns>The manifest that will be written.</returns>
    public IpmManifest BuildManifest()
    {
        var files = _payload
            .Where(file =>
            {
                return file.Pinned;
            })
            .Select(file =>
            {
                return new IpmFileEntry(file.Name, Hex(file.Bytes), file.Bytes.LongLength);
            })
            .ToList();

        var spaEntry = files.First(file =>
        {
            return file.Path == SpaEntryName;
        });

        var manifest = new IpmManifest(
            FormatVersion: 1,
            Id: "hello",
            DisplayNameKey: "HelloName",
            Version: "1.2.0",
            Tier: LicenceTier.AddOn,
            Audience: ModuleAudience.AdministratorOnly,
            MinPanelVersion: "1.0.0",
            MaxPanelVersion: null,
            NotBefore: null,
            SchemaName: "hello",
            AgentCapabilities: [AgentCapability.Sites],
            Permissions: [AuthorizationPolicies.AdminOnly],
            Menu: [new ModuleMenuItem("hello.main", "hello", ModuleIconKeys.House, "HelloMenu", 100, ModuleAudience.AdministratorOnly)],
            Routes: [new ModuleRoute("hello", "/hello", "HelloTitle", null)],
            Dependencies: [],
            Migrations: new IpmMigrationRange(null, "20261107000000_Initial"),
            EntryAssembly: AssemblyName,
            Spa: new IpmSpaInfo(
                SpaEntryName,
                "sha384-" + Convert.ToBase64String(Convert.FromHexString(spaEntry.Sha384)),
                ["en"]),
            Encryption: null,
            Files: files);

        return _edit(manifest);
    }

    /// <summary>Writes the package.</summary>
    /// <returns>The bytes of the <c>.ipm</c>.</returns>
    public byte[] Build()
    {
        var manifestBytes = IpmManifestCodec.Serialize(BuildManifest());
        var signature = IpmSignatureSigner.Sign(manifestBytes, _signer ?? _key).Value;

        if (_tamperManifest)
        {
            manifestBytes[manifestBytes.Length / 2] ^= 0x01;
        }

        using var memory = new MemoryStream();

        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true, _nameEncoding))
        {
            Write(zip, "ipm.json", manifestBytes, null);

            if (!_unsigned)
            {
                Write(zip, "ipm.sig", signature, null);
            }

            foreach (var file in _payload.Where(candidate =>
                     {
                         return candidate.Stored;
                     }))
            {
                Write(zip, file.Name, file.StoredBytes ?? file.Bytes, null);
            }

            foreach (var raw in _raw)
            {
                Write(zip, raw.Name, raw.Bytes, raw.ExternalAttributes);
            }
        }

        return memory.ToArray();
    }

    /// <summary>The SHA-384 of some bytes as lowercase hexadecimal, the spelling the manifest pins use.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The digest.</returns>
    public static string Hex(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA384.HashData(bytes));
    }

    /// <summary>Applies a change to the payload file with a name.</summary>
    /// <param name="name">The entry name.</param>
    /// <param name="change">The change.</param>
    private void Replace(string name, Func<IpmPayloadFile, IpmPayloadFile> change)
    {
        var index = _payload.FindIndex(file =>
        {
            return file.Name == name;
        });

        _payload[index] = change(_payload[index]);
    }

    /// <summary>Writes one entry with a fixed time so two builds are the same bytes.</summary>
    /// <param name="zip">The archive being written.</param>
    /// <param name="name">The entry name.</param>
    /// <param name="bytes">Its content.</param>
    /// <param name="externalAttributes">The external attributes, or null.</param>
    private static void Write(ZipArchive zip, string name, byte[] bytes, int? externalAttributes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        if (externalAttributes is not null)
        {
            entry.ExternalAttributes = externalAttributes.Value;
        }

        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }
}
