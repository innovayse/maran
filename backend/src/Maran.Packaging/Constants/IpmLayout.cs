namespace Maran.Packaging.Constants;

/// <summary>
/// The fixed names and prefixes of the <c>.ipm</c> format: a ZIP archive whose entries are the manifest,
/// its signature and the payload the manifest pins. Everything that decides whether an entry belongs in
/// a package is spelled here once, so the reader, the manifest rules and the pin check cannot disagree.
/// </summary>
public static class IpmLayout
{
    /// <summary>The only <c>formatVersion</c> this library reads.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>The manifest entry. Its stored bytes are exactly the bytes that are signed.</summary>
    public const string ManifestEntry = "ipm.json";

    /// <summary>
    /// The signature entry: the DER encoding of an ECDSA P-384 signature over the stored bytes of
    /// <see cref="ManifestEntry"/>, with SHA-384 as the digest. Neither this entry nor the manifest is
    /// listed in the manifest's <c>files</c>: the manifest cannot hold its own hash.
    /// </summary>
    public const string SignatureEntry = "ipm.sig";

    /// <summary>The encrypted library entry. Reserved here; decrypting it is the loader's, not this library's.</summary>
    public const string EncryptedLibraryEntry = "lib.enc";

    /// <summary>The folder of the module's assemblies, when the library is not encrypted.</summary>
    public const string LibraryPrefix = "lib/";

    /// <summary>The folder of the module's static interface bundle.</summary>
    public const string SpaPrefix = "spa/";

    /// <summary>The folder of the interface bundle's translations, one <c>&lt;locale&gt;.json</c> each.</summary>
    public const string SpaLocalesPrefix = "spa/locales/";

    /// <summary>The locale an interface bundle must always carry, because it is the fallback.</summary>
    public const string FallbackLocale = "en";

    /// <summary>The length of a SHA-384 digest written as lowercase hexadecimal.</summary>
    public const int Sha384HexLength = 96;

    /// <summary>The prefix of an interface bundle's integrity value, followed by the base64 SHA-384 of its entry file.</summary>
    public const string IntegrityPrefix = "sha384-";
}
