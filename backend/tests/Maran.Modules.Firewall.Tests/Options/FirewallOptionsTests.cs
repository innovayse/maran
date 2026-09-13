using Maran.Modules.Firewall.Options;

namespace Maran.Modules.Firewall.Tests.Options;

/// <summary>
/// How the one environment variable the installer writes becomes the port list every agent call
/// carries.
/// </summary>
public sealed class FirewallOptionsTests
{
    /// <summary>A comma separated list is parsed in the order it was written.</summary>
    [Fact]
    public void A_comma_separated_list_is_parsed_in_the_order_it_was_written()
    {
        // The shape the installer actually writes for a host whose sshd_config includes a drop-in
        // that adds ports.
        var options = new FirewallOptions { SshPorts = "22,2200,2222" };

        Assert.Equal([22, 2200, 2222], options.SshPortNumbers);
    }

    /// <summary>A single port is parsed.</summary>
    [Fact]
    public void A_single_port_is_parsed()
    {
        Assert.Equal([2222], new FirewallOptions { SshPorts = "2222" }.SshPortNumbers);
    }

    /// <summary>Whitespace around a port is ignored.</summary>
    [Fact]
    public void Whitespace_around_a_port_is_ignored()
    {
        // panel.env is edited by hand often enough that a space after a comma must not be a lockout.
        Assert.Equal([22, 2222], new FirewallOptions { SshPorts = " 22 , 2222 " }.SshPortNumbers);
    }

    /// <summary>A list holding anything that is not a port yields no ports at all.</summary>
    [Theory]
    [InlineData("22,notaport")]
    [InlineData("22,0")]
    [InlineData("22,65536")]
    [InlineData("22,-1")]
    public void A_list_holding_anything_that_is_not_a_port_yields_no_ports_at_all(string configured)
    {
        // All or nothing. Keeping the entries that parsed would render a ruleset allowing SOME of
        // the host's SSH ports, which is a lockout for whoever is connected on one of the others —
        // and the panel would have started cleanly and said nothing.
        Assert.Empty(new FirewallOptions { SshPorts = configured }.SshPortNumbers);
    }

    /// <summary>An absent setting yields no ports and never a default.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_setting_yields_no_ports_and_never_a_default(string configured)
    {
        // The value 22 must not appear from nowhere: on a host serving sshd on 2222 alone it would
        // render an accept for a port nothing listens on and none for the port the operator is
        // connected through.
        Assert.Empty(new FirewallOptions { SshPorts = configured }.SshPortNumbers);
    }

    /// <summary>A fresh options object carries no ssh ports and no panel port.</summary>
    [Fact]
    public void A_fresh_options_object_carries_no_ssh_ports_and_no_panel_port()
    {
        // The ABSENCE of a default is the safety property, so it is asserted rather than assumed. A
        // 22 sitting in this initialiser would let a panel whose panel.env never mentioned
        // Firewall__SshPorts start cleanly and then render a firewall for a port sshd may not be
        // listening on — while rendering none for the port the operator is connected through.
        var options = new FirewallOptions();

        Assert.Equal(string.Empty, options.SshPorts);
        Assert.Empty(options.SshPortNumbers);
        Assert.Equal(0, options.PanelPort);
        Assert.Equal(string.Empty, options.SeedWhitelistCidr);
    }

    /// <summary>Port zero is not a usable port.</summary>
    [Fact]
    public void Port_zero_is_not_a_usable_port()
    {
        // Zero is the proto3 default of every port field on the agent contract, so it is what
        // "nobody set this" looks like once it reaches the wire.
        Assert.False(FirewallOptions.IsUsablePort(0));
        Assert.True(FirewallOptions.IsUsablePort(1));
        Assert.True(FirewallOptions.IsUsablePort(65535));
        Assert.False(FirewallOptions.IsUsablePort(65536));
    }
}
