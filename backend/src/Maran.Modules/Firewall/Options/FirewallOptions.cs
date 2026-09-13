using System.Globalization;

namespace Maran.Modules.Firewall.Options;

/// <summary>
/// The two host facts nothing inside the panel can see, and the seed the installer leaves behind.
/// Bound from the <c>Firewall</c> configuration section, which the installer writes into
/// <c>panel.env</c> as <c>Firewall__SshPorts</c>, <c>Firewall__PanelPort</c> and
/// <c>Firewall__SeedWhitelistCidr</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This class is the only thing standing between the panel and a locked-out server.</b> The agent
/// re-renders the WHOLE nftables ruleset on every mutation and the rendered policy is drop, so the
/// ports named here decide whether the operator's own SSH session and the panel itself survive the
/// next rule change. Nothing here has a working default, and nothing may be given one:
/// <c>FirewallOptionsValidator</c> refuses the boot instead, because a silently-defaulted port IS
/// the lockout — the panel comes up, serves happily, and dies on the first firewall change with no
/// remote way back in.
/// </para>
/// <para>
/// <b><see cref="SshPorts"/> is a LIST, and the plural is the safety property.</b> sshd listens on
/// EVERY <c>Port</c> directive it is given and on every <c>ListenAddress host:port</c>, across
/// <c>sshd_config</c> and everything its <c>Include</c> pulls in. Ubuntu and Debian ship
/// <c>Include /etc/ssh/sshd_config.d/*.conf</c> as the first line, so a reader that stopped at the
/// main file reports 22 for a host serving sshd on 2222 alone — four of the eight supported targets
/// ship that shape. The union is allowed on purpose: a port sshd is not using costs nothing to
/// allow, and one it IS using costs the server.
/// </para>
/// <para>
/// <b><see cref="PanelPort"/> is nginx's public port, not Kestrel's.</b> The public vhost is 8443.
/// On a server the API has no port at all — it listens on the unix socket <c>/run/maran-api/api.sock</c>
/// behind nginx, which is what stops any local process reaching it — and it is only in development
/// that <c>ASPNETCORE_URLS</c> names loopback 5080. Either way that value is never this one:
/// rendering <c>tcp dport 5080 accept</c> under a drop policy leaves the panel reachable right after
/// the installer's seed — nginx's port having survived only because nothing was dropping yet — and
/// unreachable the moment anything changes a rule, with nobody able to sign in and undo it.
/// </para>
/// </remarks>
public sealed class FirewallOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "Firewall";

    /// <summary>The environment variable the installer writes <see cref="SshPorts"/> as.</summary>
    /// <remarks>
    /// Spelled out so the startup refusal can name the key an operator has to fix. A message saying
    /// "SshPorts is required" sends them looking through appsettings; this one sends them to
    /// <c>/etc/maran/panel.env</c>, which is where the value actually lives.
    /// </remarks>
    public const string SshPortsEnvironmentKey = "Firewall__SshPorts";

    /// <summary>The environment variable the installer writes <see cref="PanelPort"/> as.</summary>
    public const string PanelPortEnvironmentKey = "Firewall__PanelPort";

    /// <summary>The lowest number that can be a port.</summary>
    private const int LowestPort = 1;

    /// <summary>The highest number that can be a port.</summary>
    private const int HighestPort = 65535;

    /// <summary>What separates one port from the next in <see cref="SshPorts"/>.</summary>
    private const char PortSeparator = ',';

    /// <summary>
    /// Every port this host's sshd listens on, comma-separated and ascending, exactly as the
    /// installer detected them: <c>22</c>, or <c>22,2200,2222</c>.
    /// </summary>
    /// <remarks>
    /// A string and not an <c>int[]</c> because of how it arrives. The .NET configuration binder
    /// fills an array from INDEXED keys (<c>Firewall__SshPorts__0</c>), and the installer writes one
    /// environment variable holding a list — as every other multi-valued setting in
    /// <c>panel.env</c> does, and as an operator editing that file by hand would expect. Bound as
    /// written, split by <see cref="SshPortNumbers"/>, and refused at startup when it cannot be.
    /// </remarks>
    public string SshPorts { get; set; } = string.Empty;

    /// <summary>The public port the panel's own nginx vhost listens on.</summary>
    public int PanelPort { get; set; }

    /// <summary>
    /// The address the installer was run from, as a single-host CIDR, or empty when the install saw
    /// none.
    /// </summary>
    /// <remarks>
    /// Read exactly once, by <c>WhitelistSeeder</c>, which records that it has read it in a row of
    /// its own. From then on the whitelist is panel data and editing this value changes nothing —
    /// which the installer's own comment in <c>panel.env</c> promises, so it must stay true, and
    /// "while the whitelist is empty" was not enough to keep it: deleting the seeded row emptied the
    /// whitelist and the next restart put the exemption back.
    /// </remarks>
    public string SeedWhitelistCidr { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="SshPorts"/> as the numbers every agent call takes, in the order they were written.
    /// </summary>
    /// <returns>
    /// The ports, or an EMPTY list when the setting is absent or holds anything that is not a port —
    /// the state <c>FirewallOptionsValidator</c> refuses the boot for, and the state the agent client
    /// refuses to send. Nothing here substitutes 22 for a value that failed to arrive: an accept
    /// rendered for a port nothing listens on, and none for the port the operator is connected
    /// through, is the lockout wearing a plausible number.
    /// </returns>
    public IReadOnlyList<int> SshPortNumbers
    {
        get
        {
            return ParsePorts(SshPorts);
        }
    }

    /// <summary>Whether <paramref name="port"/> is a number a rule may name at all.</summary>
    /// <param name="port">The candidate.</param>
    /// <returns>True for 1-65535.</returns>
    /// <remarks>
    /// Zero is excluded because it is the proto3 default of every port field on the agent contract:
    /// it is what "nobody set this" looks like by the time it reaches the wire, and the one value
    /// that must never travel as though somebody had chosen it.
    /// </remarks>
    public static bool IsUsablePort(int port)
    {
        return port is >= LowestPort and <= HighestPort;
    }

    /// <summary>Splits the configured value into ports, or gives up whole.</summary>
    /// <param name="raw">The value bound from <c>Firewall__SshPorts</c>.</param>
    /// <returns>The ports, or an empty list when any entry is not one.</returns>
    /// <remarks>
    /// All or nothing, deliberately. Dropping the entries that did not parse and keeping the rest
    /// would render a ruleset that allows SOME of the host's SSH ports, which is a lockout for
    /// whoever is connected on one of the others — and it would do it while the panel started
    /// cleanly and reported nothing.
    /// </remarks>
    private static List<int> ParsePorts(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var parts = raw.Split(PortSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var ports = new List<int>(parts.Length);

        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
                || !IsUsablePort(port))
            {
                return [];
            }

            ports.Add(port);
        }

        return ports;
    }
}
