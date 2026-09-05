using System.Globalization;
using Maran.Modules.Firewall.Options;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Firewall.Validators;

/// <summary>
/// Refuses the boot when the host facts the firewall needs did not arrive (rules/csharp.md "Options
/// validated at startup"). Registered with <c>ValidateOnStart</c>, so a panel missing them never
/// serves a request.
/// </summary>
/// <remarks>
/// <para>
/// <b>Refusing is the whole point, and defaulting is the bug this prevents.</b> The agent re-renders
/// the entire nftables ruleset on every mutation under a drop policy. A panel that started with an
/// empty SSH port list would run perfectly until the first firewall change and then cut off both the
/// operator's session and the panel, with no remote recovery path — and it would do it having
/// reported nothing wrong. A boot that stops with a message naming the two environment variables is
/// a five-minute fix; the alternative is a trip to a console or a rebuild.
/// </para>
/// <para>
/// The messages name <c>Firewall__SshPorts</c> and <c>Firewall__PanelPort</c> rather than the
/// configuration paths they bind to, because those are the strings the reader will find in the file
/// they have to edit.
/// </para>
/// <para>
/// <b>Two readers, opposite causes, one exception.</b> On a server this fires because
/// <c>/etc/maran/panel.env</c> is wrong or was never written; on a workstation it fires because the
/// developer's git-ignored <c>.env</c> predates these keys, since <c>scripts/dev</c> seeds it from
/// <c>.env.example</c> once and never again. Naming only the key would leave both of them hunting,
/// so every message below names BOTH files and says which is the authority for the value — the
/// installer's <c>panel.env.example</c>, which is where the development defaults were copied from
/// and where they must stay identical, or a developer's local behaviour diverges from a real host in
/// exactly the area where divergence is expensive.
/// </para>
/// <para>
/// An <see cref="IValidateOptions{TOptions}"/> rather than a <c>Validate(predicate, message)</c>
/// callback: that overload carries ONE fixed sentence, and the three ways this configuration can be
/// wrong — no ports, a port that is not a port, no panel port — need three different sentences to be
/// worth reading at four in the morning.
/// </para>
/// </remarks>
public sealed class FirewallOptionsValidator : IValidateOptions<FirewallOptions>
{
    /// <inheritdoc />
    /// <remarks>
    /// Every problem is reported, not merely the first. A reader whose file is missing both values
    /// should learn that in one boot rather than two.
    /// </remarks>
    public ValidateOptionsResult Validate(string? name, FirewallOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.SshPorts))
        {
            failures.Add(
                $"{FirewallOptions.SshPortsEnvironmentKey} is empty. It must list every port this "
                + "host's sshd listens on, comma-separated (for example 22,2200,2222). It is not "
                + "defaulted to 22, because a firewall rendered for a port sshd is not using — and "
                + "not for the one it is — locks this server's administrator out with no remote way "
                + "back in. On a server it belongs in /etc/maran/panel.env, which the installer "
                + "writes; on a workstation, in the repository's .env, which scripts/dev seeds from "
                + ".env.example once — an .env made before this key existed will not have it. "
                + "installer/panel.env.example is the authority for the value.");
        }
        else if (options.SshPortNumbers.Count == 0)
        {
            failures.Add(
                $"{FirewallOptions.SshPortsEnvironmentKey} is '{options.SshPorts}', which is not a "
                + "comma-separated list of ports in 1-65535. The whole value is refused rather than "
                + "the entries that failed to parse: allowing some of the host's SSH ports is a "
                + "lockout for whoever is connected on one of the others.");
        }

        if (!FirewallOptions.IsUsablePort(options.PanelPort))
        {
            var panelPort = options.PanelPort.ToString(CultureInfo.InvariantCulture);
            failures.Add(
                $"{FirewallOptions.PanelPortEnvironmentKey} is {panelPort}, which is not a port in "
                + "1-65535. It is the PUBLIC port of the panel's nginx vhost (8443 by default), never "
                + "whatever ASPNETCORE_URLS names — a unix socket on a server, a loopback port in "
                + "development. Opening a loopback port here leaves the panel reachable until the "
                + "first rule change and unreachable afterwards. On a server it belongs in "
                + "/etc/maran/panel.env; on a workstation, in the repository's .env, seeded from "
                + ".env.example. installer/panel.env.example is the authority for the value.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
