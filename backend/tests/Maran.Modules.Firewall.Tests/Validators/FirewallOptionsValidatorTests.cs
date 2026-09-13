using Maran.Modules.Firewall.Options;
using Maran.Modules.Firewall.Validators;

namespace Maran.Modules.Firewall.Tests.Validators;

/// <summary>
/// What the panel does when the host facts the firewall needs did not arrive: it refuses to start,
/// and says which environment variable to fix.
/// </summary>
public sealed class FirewallOptionsValidatorTests
{
    /// <summary>A panel that was told no ssh ports refuses to start.</summary>
    [Fact]
    public void A_panel_that_was_told_no_ssh_ports_refuses_to_start()
    {
        // The alternative is not a smaller failure, it is a bigger one: a panel that starts with no
        // SSH ports serves perfectly until the first firewall change and then cuts off both the
        // operator's session and itself, with no remote recovery path.
        var result = new FirewallOptionsValidator()
            .Validate(null, new FirewallOptions { SshPorts = string.Empty, PanelPort = 8443 });

        Assert.True(result.Failed);
    }

    /// <summary>The refusal names the environment variable a reader has to fix.</summary>
    [Fact]
    public void The_refusal_names_the_environment_variable_a_reader_has_to_fix()
    {
        // A message saying "SshPorts is required" sends its reader looking through appsettings.
        var result = new FirewallOptionsValidator()
            .Validate(null, new FirewallOptions { SshPorts = string.Empty, PanelPort = 8443 });

        Assert.Contains("Firewall__SshPorts", result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>The refusal names both files it can be fixed in and the authority for the value.</summary>
    [Fact]
    public void The_refusal_names_both_files_it_can_be_fixed_in_and_the_authority_for_the_value()
    {
        // The same exception reaches two readers from opposite causes: an operator whose panel.env is
        // wrong, and a developer whose git-ignored .env predates the key because scripts/dev seeds it
        // from .env.example once. A message naming only one of them leaves the other hunting.
        var result = new FirewallOptionsValidator()
            .Validate(null, new FirewallOptions { SshPorts = string.Empty, PanelPort = 0 });

        Assert.Contains("/etc/maran/panel.env", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains(".env.example", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("installer/panel.env.example", result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>A panel that was told a zero panel port refuses to start.</summary>
    [Fact]
    public void A_panel_that_was_told_a_zero_panel_port_refuses_to_start()
    {
        var result = new FirewallOptionsValidator()
            .Validate(null, new FirewallOptions { SshPorts = "22", PanelPort = 0 });

        Assert.True(result.Failed);
        Assert.Contains("Firewall__PanelPort", result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>The panel port refusal warns against the api's own listen address.</summary>
    [Fact]
    public void The_panel_port_refusal_warns_against_the_apis_own_listen_address()
    {
        // The literal reading of "the backend's own listen port" is whatever ASPNETCORE_URLS names:
        // a unix socket on a server, and 5080 in development. Rendering an accept for the
        // development number under a drop policy leaves the panel reachable right after the
        // installer's seed and dead on the first rule change. The message an operator reads has to
        // name the setting so neither reading survives.
        var result = new FirewallOptionsValidator()
            .Validate(null, new FirewallOptions { SshPorts = "22", PanelPort = 0 });

        Assert.Contains("ASPNETCORE_URLS", result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>An ssh port list that cannot be read refuses to start.</summary>
    [Fact]
    public void An_ssh_port_list_that_cannot_be_read_refuses_to_start()
    {
        var result = new FirewallOptionsValidator()
            .Validate(null, new FirewallOptions { SshPorts = "22,notaport", PanelPort = 8443 });

        Assert.True(result.Failed);
        Assert.Contains("Firewall__SshPorts", result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>Both problems are reported in one boot rather than in two.</summary>
    [Fact]
    public void Both_problems_are_reported_in_one_boot_rather_than_in_two()
    {
        var result = new FirewallOptionsValidator()
            .Validate(null, new FirewallOptions { SshPorts = string.Empty, PanelPort = 0 });

        Assert.Contains("Firewall__SshPorts", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Firewall__PanelPort", result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>A panel told both host facts starts.</summary>
    [Fact]
    public void A_panel_told_both_host_facts_starts()
    {
        // Guards every refusal above from passing for the wrong reason: a validator that refused
        // everything would satisfy them all.
        var result = new FirewallOptionsValidator()
            .Validate(null, new FirewallOptions { SshPorts = "22,2222", PanelPort = 8443 });

        Assert.True(result.Succeeded);
    }
}
