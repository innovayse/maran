using Maran.Modules.Mail.Models;

namespace Maran.Modules.Mail.Interfaces;

/// <summary>Makes the key pair a mail domain signs its outgoing mail with.</summary>
/// <remarks>
/// A seam so handler tests need not generate a 2048-bit RSA key (hundreds of milliseconds) per case, and
/// so the one implementation that does can be tested for what it must produce.
/// </remarks>
public interface IDkimKeyGenerator
{
    /// <summary>Generates a fresh RSA key pair.</summary>
    /// <returns>The private key as PEM in a non-printing wrapper, and the public key as the DNS record carries it.</returns>
    GeneratedDkimKey Generate();
}
