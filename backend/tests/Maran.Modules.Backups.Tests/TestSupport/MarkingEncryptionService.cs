using System.Text;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Backups.Tests.TestSupport;

/// <summary>
/// A reversible stand-in for the panel's cipher whose output is recognisably not the input, so a test
/// can tell a value that went through the encrypted column from one that was stored as it came.
/// </summary>
/// <remarks>
/// It protects nothing, and nothing but tests constructs it. A pass-through fake would make the
/// archive key's column look encrypted while storing it in the clear, and a test asserting "the
/// stored value is not the key" would pass for the wrong reason or not be writable at all.
/// </remarks>
public sealed class MarkingEncryptionService : IEncryptionService
{
    /// <summary>The prefix that marks a value as having been through <see cref="Encrypt"/>.</summary>
    public const string Marker = "sealed:";

    /// <inheritdoc />
    public string Encrypt(string plainText)
    {
        return Marker + Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
    }

    /// <inheritdoc />
    public string Decrypt(string cipherText)
    {
        if (!cipherText.StartsWith(Marker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The value was not stored through the encrypted column.");
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(cipherText[Marker.Length..]));
    }
}
