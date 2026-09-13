using System.ComponentModel.DataAnnotations;

namespace Maran.Modules.Identity.Options;

/// <summary>
/// Settings for the access tokens this module issues and the Host validates. Bound from the
/// <c>Jwt</c> configuration section; the signing key comes from <c>/etc/maran/panel.env</c> in
/// production, never a committed <c>appsettings.json</c> (rules/security.md "Secrets").
/// </summary>
/// <remarks>
/// The type lives in this module rather than in <c>Maran.Host/Configuration/</c>, where the panel's
/// other options classes sit, because both sides of the token need it: this module signs with it
/// and the Host's bearer handler validates against it. The Host references every module, so it can
/// read this; a module can never read the Host, so the reverse placement would not compile.
/// </remarks>
public sealed class JwtOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "Jwt";

    /// <summary>Required minimum decoded length of <see cref="SigningKey"/>, in bytes (HMAC-SHA256).</summary>
    private const int MinimumKeyBytes = 32;

    /// <summary>
    /// Base64-encoded key the access tokens are signed with. Data annotations cannot measure the
    /// decoded length, so the options registration also runs a callback over
    /// <see cref="HasValidSigningKey"/>, failing the boot rather than the first login.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>The <c>iss</c> claim written and demanded.</summary>
    [Required]
    public string Issuer { get; set; } = "maran";

    /// <summary>The <c>aud</c> claim written and demanded.</summary>
    [Required]
    public string Audience { get; set; } = "maran-panel";

    /// <summary>How long an access token stays valid, in minutes (spec §10: fifteen).</summary>
    [Range(1, 60)]
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>How long a refresh token stays valid, in days.</summary>
    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>
    /// Decodes <see cref="SigningKey"/> and checks it is at least <see cref="MinimumKeyBytes"/>
    /// bytes. Returns false rather than throwing so the caller can surface a clear
    /// <see cref="Microsoft.Extensions.Options.OptionsValidationException"/> message.
    /// </summary>
    /// <returns>True when the key decodes and is long enough to sign with.</returns>
    public bool HasValidSigningKey()
    {
        if (string.IsNullOrWhiteSpace(SigningKey))
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(SigningKey).Length >= MinimumKeyBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
