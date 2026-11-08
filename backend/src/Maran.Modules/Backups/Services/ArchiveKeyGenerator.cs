using System.Security.Cryptography;
using Maran.Modules.Backups.Interfaces;

namespace Maran.Modules.Backups.Services;

/// <summary>Makes the key a backup destination's archives are sealed with.</summary>
/// <remarks>
/// <para>
/// 256 random bits from the operating system's cryptographic generator, written as 64 lowercase
/// hexadecimal characters — the spelling the agent's <c>ArchiveKey</c> accepts and nothing else. The
/// key is random and not derived from anything: not from a passphrase an operator could forget, not
/// from the panel's own key (a derivation would make the panel key's rotation silently change which
/// key opens which backup). It is stored under the panel's cipher the first time encryption is
/// switched on and read back by the resolver from then on.
/// </para>
/// <para>
/// A service behind <see cref="IArchiveKeyGenerator"/> and not a static helper because it has an effect
/// (it draws randomness), and a test that needs a fixed key can supply its own implementation.
/// </para>
/// </remarks>
public sealed class ArchiveKeyGenerator : IArchiveKeyGenerator
{
    /// <summary>The key's length in bytes: the cipher's key size.</summary>
    public const int KeyBytes = 32;

    /// <inheritdoc/>
    public string Mint()
    {
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(KeyBytes));
    }
}
