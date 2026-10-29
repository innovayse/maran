namespace Maran.Host.Tests;

/// <summary>
/// Configuration every host-backed test must supply. Startup validation deliberately refuses to
/// boot a misconfigured panel, so a test host needs the same minimum a real deployment does.
/// </summary>
public static class PanelTestSettings
{
    /// <summary>
    /// The development encryption key this repository publishes. Startup refuses it outside the
    /// Development and Testing environments; test hosts run as Testing, which is the only reason it
    /// is accepted. It protects nothing and must never be used on a real server.
    /// </summary>
    public const string EncryptionKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Configuration key the encryption key binds from.</summary>
    public const string EncryptionKeyPath = "Security:EncryptionKey";

    /// <summary>
    /// The development signing key this repository publishes, accepted only because test hosts run
    /// as Testing (startup refuses it elsewhere). It protects nothing and must never be used on a
    /// real server.
    /// </summary>
    public const string JwtSigningKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Configuration key the signing key binds from.</summary>
    public const string JwtSigningKeyPath = "Jwt:SigningKey";

    /// <summary>
    /// A host with one SSH port, which is what the Firewall module refuses to start without.
    /// </summary>
    /// <remarks>
    /// Not a default the product has — it deliberately has none, because a firewall rendered for a
    /// port sshd is not using locks the server's administrator out. It is a value this test host
    /// STATES, the same way it states an encryption key, so that startup validation passes for the
    /// reason a real deployment's does: somebody supplied the fact.
    /// </remarks>
    public const string FirewallSshPorts = "22";

    /// <summary>Configuration key the SSH ports bind from.</summary>
    public const string FirewallSshPortsPath = "Firewall:SshPorts";

    /// <summary>The panel's public nginx port, which the Firewall module also refuses to start without.</summary>
    public const string FirewallPanelPort = "8443";

    /// <summary>Configuration key the panel port binds from.</summary>
    public const string FirewallPanelPortPath = "Firewall:PanelPort";
}
