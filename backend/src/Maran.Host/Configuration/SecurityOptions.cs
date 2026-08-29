using System.ComponentModel.DataAnnotations;

namespace Maran.Host.Configuration;

/// <summary>
/// Security-sensitive settings that MUST fail the boot rather than the first request when
/// missing or malformed (rules/security.md "Secrets"). Bound from the <c>Security</c>
/// configuration section; the encryption key is supplied by <c>/etc/maran/panel.env</c> in
/// production, never a committed <c>appsettings.json</c>.
/// </summary>
public sealed class SecurityOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "Security";

    /// <summary>Required base64-decoded length of <see cref="EncryptionKey"/>, in bytes (AES-256).</summary>
    private const int RequiredKeyBytes = 32;

    /// <summary>
    /// Base64-encoded 256-bit key used by <c>AesGcmEncryptionService</c> to encrypt secrets at
    /// rest. Data annotations alone cannot check the decoded length, so the options registration
    /// also runs a <c>Validate</c> callback (see <see cref="HasValidEncryptionKey"/>) that decodes and
    /// measures it, failing startup rather than the first request.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string EncryptionKey { get; set; } = string.Empty;

    /// <summary>
    /// Decodes <see cref="EncryptionKey"/> and checks it is exactly <see cref="RequiredKeyBytes"/>
    /// bytes once decoded. Used by the startup validation callback; returns false rather than
    /// throwing so the caller can surface a clear <see cref="Microsoft.Extensions.Options.OptionsValidationException"/> message.
    /// </summary>
    public bool HasValidEncryptionKey()
    {
        if (string.IsNullOrWhiteSpace(EncryptionKey))
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(EncryptionKey).Length == RequiredKeyBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
