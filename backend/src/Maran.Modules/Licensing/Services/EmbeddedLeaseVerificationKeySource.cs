using System.Reflection;
using Maran.Modules.Licensing.Domain.ValueObjects;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Reads the production lease verification key the release embedded into this assembly, or answers
/// <see cref="LeaseVerificationKey.None"/> when the build carries none.
/// </summary>
/// <remarks>
/// <c>installer/keys/lease-verification.pub</c> holds the 64 hexadecimal characters of the public key
/// and is embedded by this project's csproj when the file exists. The file does not exist until the
/// issuing service has generated the lease key pair offline (its private half never enters this
/// repository), and until then every build refuses every lease. No placeholder is ever embedded. The
/// shape and the reasoning are those of <see cref="EmbeddedLicenceVerificationKeySource"/>; the key is
/// a different one on purpose.
/// </remarks>
public static class EmbeddedLeaseVerificationKeySource
{
    /// <summary>The logical name the csproj gives the embedded key file.</summary>
    private const string ResourceName = "lease-verification.pub";

    /// <summary>Reads the key embedded in this assembly.</summary>
    /// <returns>The embedded key, or <see cref="LeaseVerificationKey.None"/>.</returns>
    public static LeaseVerificationKey Read()
    {
        return Read(typeof(EmbeddedLeaseVerificationKeySource).Assembly);
    }

    /// <summary>Reads the key embedded in the given assembly.</summary>
    /// <param name="assembly">The assembly to look in.</param>
    /// <returns>The embedded key, or <see cref="LeaseVerificationKey.None"/>.</returns>
    public static LeaseVerificationKey Read(Assembly assembly)
    {
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return LeaseVerificationKey.None;
        }

        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Parses the key file's text.</summary>
    /// <param name="text">The file's text: 64 hexadecimal characters, surrounding whitespace allowed.</param>
    /// <returns>The key, or <see cref="LeaseVerificationKey.None"/> when the text is anything else.</returns>
    public static LeaseVerificationKey Parse(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length != LeaseVerificationKey.PublicKeyLength * 2
            || !trimmed.All(character => { return char.IsAsciiHexDigit(character); }))
        {
            return LeaseVerificationKey.None;
        }

        return LeaseVerificationKey.Of(Convert.FromHexString(trimmed));
    }
}
