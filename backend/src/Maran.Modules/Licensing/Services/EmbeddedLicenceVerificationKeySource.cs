using System.Reflection;
using Maran.Modules.Licensing.Domain.ValueObjects;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Reads the production licence verification key the release embedded into this assembly, or
/// answers <see cref="LicenceVerificationKey.None"/> when the build carries none.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where the key comes from.</b> <c>installer/keys/licence-verification.pub</c> — the licence
/// counterpart of <c>release-signing.pub</c> — holds the 64 hexadecimal characters of the public key
/// and is embedded at build time by this project's csproj when the file exists. It does not exist
/// until the real key pair has been generated offline by the issuing service, and until then every
/// build trusts no key and refuses every licence. No placeholder is ever embedded: a placeholder
/// key whose private half is public is a licence anyone can forge
/// (<c>docs/superpowers/notes/2026-09-22-licence-verification-threat-note.md</c> §4).
/// </para>
/// <para>
/// A resource that is present but unreadable (not 64 hexadecimal characters) is also "no key":
/// this runs while the panel composes, where throwing would take the whole panel down (the module's
/// §229 boundary), and "no key" refuses every licence, which is the safe direction.
/// </para>
/// </remarks>
public static class EmbeddedLicenceVerificationKeySource
{
    /// <summary>The logical name the csproj gives the embedded key file.</summary>
    private const string ResourceName = "licence-verification.pub";

    /// <summary>Reads the key embedded in this assembly.</summary>
    /// <returns>The embedded key, or <see cref="LicenceVerificationKey.None"/>.</returns>
    public static LicenceVerificationKey Read()
    {
        return Read(typeof(EmbeddedLicenceVerificationKeySource).Assembly);
    }

    /// <summary>Reads the key embedded in the given assembly.</summary>
    /// <param name="assembly">The assembly to look in.</param>
    /// <returns>The embedded key, or <see cref="LicenceVerificationKey.None"/>.</returns>
    public static LicenceVerificationKey Read(Assembly assembly)
    {
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return LicenceVerificationKey.None;
        }

        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Parses the key file's text.</summary>
    /// <param name="text">The file's text: 64 hexadecimal characters, surrounding whitespace allowed.</param>
    /// <returns>The key, or <see cref="LicenceVerificationKey.None"/> when the text is anything else.</returns>
    public static LicenceVerificationKey Parse(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length != LicenceVerificationKey.PublicKeyLength * 2
            || !trimmed.All(character => { return char.IsAsciiHexDigit(character); }))
        {
            return LicenceVerificationKey.None;
        }

        return LicenceVerificationKey.Of(Convert.FromHexString(trimmed));
    }
}
