using System.Net;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.SharedKernel.Tests.Utilities.Network;

/// <summary>Which typed hosts name the machine itself: loopback, its interface addresses and its own names.</summary>
public sealed class ThisMachineRuleTests
{
    /// <summary>A machine with one IPv4 address, one IPv6 address and one name.</summary>
    private static readonly ThisMachineFacts Machine =
        new([IPAddress.Parse("198.51.100.20"), IPAddress.Parse("2001:db8::20")], ["panel.host.test"]);

    /// <summary>Loopback in every spelling, the machine's addresses and its name are this machine.</summary>
    [Theory]
    [InlineData("localhost")]
    [InlineData("LocalHost.")]
    [InlineData("127.0.0.1")]
    [InlineData("127.1")]
    [InlineData("::1")]
    [InlineData("[::1]")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("198.51.100.20")]
    [InlineData("::ffff:198.51.100.20")]
    [InlineData("2001:db8::20")]
    [InlineData("panel.host.test")]
    [InlineData("Panel.Host.Test.")]
    public void A_host_that_names_the_machine_is_recognised(string host)
    {
        Assert.True(ThisMachineRule.IsThisMachine(host, Machine));
    }

    /// <summary>Inverse control: other addresses and names, and an empty value, are not this machine.</summary>
    [Theory]
    [InlineData("198.51.100.21")]
    [InlineData("2001:db8::21")]
    [InlineData("smtp.example.com")]
    [InlineData("host.test")]
    [InlineData("")]
    [InlineData(null)]
    public void Another_host_is_not_recognised(string? host)
    {
        Assert.False(ThisMachineRule.IsThisMachine(host, Machine));
    }

    /// <summary>A machine that could not list itself still recognises loopback.</summary>
    [Fact]
    public void A_machine_with_no_facts_still_recognises_loopback_and_nothing_else()
    {
        Assert.True(ThisMachineRule.IsThisMachine("127.0.0.1", ThisMachineFacts.None));
        Assert.False(ThisMachineRule.IsThisMachine("198.51.100.20", ThisMachineFacts.None));
    }
}
