using Maran.Modules.Firewall.Options;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The two host facts the panel refuses to boot without, in the shape a test host has to supply
/// them.
/// </summary>
/// <remarks>
/// The Firewall module validates <c>Firewall:SshPorts</c> and <c>Firewall:PanelPort</c> on start and
/// stops the host when either is missing, exactly as it does for the encryption key and the JWT
/// signing key — a silently-defaulted port would be a locked-out server, so there is no default to
/// inherit. Every factory in this project therefore states them, and they are collected here rather
/// than retyped so that the values a test host runs with are one thing to read and one thing to
/// change.
///
/// The values are the shape a real host has and not a special case: one SSH port, and nginx's public
/// port rather than Kestrel's loopback one.
/// </remarks>
public static class FirewallSettings
{
    /// <summary>The settings a test host must be given for the Firewall module to start.</summary>
    /// <returns>Configuration key/value pairs to apply with <c>UseSetting</c>.</returns>
    public static IReadOnlyList<KeyValuePair<string, string>> Required()
    {
        return
        [
            new KeyValuePair<string, string>($"{FirewallOptions.SectionName}:{nameof(FirewallOptions.SshPorts)}", "22"),
            new KeyValuePair<string, string>(
                $"{FirewallOptions.SectionName}:{nameof(FirewallOptions.PanelPort)}", "8443"),
        ];
    }
}
