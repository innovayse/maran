namespace Maran.Host.Tests;

/// <summary>
/// Configuration every host-backed test must supply. Startup validation deliberately refuses to
/// boot a misconfigured panel, so a test host needs the same minimum a real deployment does.
/// </summary>
public static class PanelTestSettings
{
    /// <summary>
    /// A throwaway base64 256-bit key. Encryption is exercised by its own unit tests; host tests
    /// only need startup validation to pass, and this value never leaves the test process.
    /// </summary>
    public const string EncryptionKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Configuration key the encryption key binds from.</summary>
    public const string EncryptionKeyPath = "Security:EncryptionKey";

    /// <summary>
    /// A throwaway base64 signing key for access tokens. Token issuing has its own unit tests; a
    /// host test only needs startup validation to accept the key.
    /// </summary>
    public const string JwtSigningKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Configuration key the signing key binds from.</summary>
    public const string JwtSigningKeyPath = "Jwt:SigningKey";
}
