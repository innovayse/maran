using Maran.Modules.Dns.Commands.DisableDns;
using Maran.Modules.Dns.Commands.EnableDns;
using Maran.Modules.Dns.Commands.SaveDnsSettings;
using Maran.Modules.Dns.Queries.GetDnsStatus;
using Maran.Modules.Dns.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Dns.Tests.Commands.Server;

/// <summary>Switching the name server on and off, and the settings every zone is published with.</summary>
public sealed class ServerCommandHandlerTests
{
    /// <summary>Builds a settings command with sound values.</summary>
    /// <returns>The command.</returns>
    private static SaveDnsSettingsCommand Settings()
    {
        return new SaveDnsSettingsCommand(
            "NS1.Panel.Test", string.Empty, "hostmaster.panel.test", "203.0.113.5", "2001:DB8:0:0:0:0:0:1", 3600, "203.0.113.1", "tests");
    }

    /// <summary>Settings are stored in one spelling and journalled.</summary>
    [Fact]
    public async Task Saving_stores_one_spelling_and_journals()
    {
        using var context = new DnsTestContext();

        var saved = await context.SaveSettings.HandleAsync(Settings(), CancellationToken.None);

        Assert.True(saved.IsSuccess);
        Assert.Equal("ns1.panel.test", saved.Value.PrimaryNameserver);
        Assert.Equal("2001:db8::1", saved.Value.PublicIpv6);
        Assert.True(saved.Value.Complete);
        Assert.False(saved.Value.Enabled);
        Assert.Equal(DnsAuditActions.DnsSettingsSaved, Assert.Single(context.Entries).Action);
    }

    /// <summary>The name server cannot be switched on before its settings are stated, and the host is not asked.</summary>
    [Fact]
    public async Task Enabling_needs_settings()
    {
        using var context = new DnsTestContext();

        var result = await context.Enable.HandleAsync(new EnableDnsCommand("203.0.113.1", "tests"), CancellationToken.None);

        Assert.Equal("DnsSettingsIncomplete", result.Error!.Code);
        Assert.Equal(0, context.Agent.EnableCalls);
    }

    /// <summary>Enabling asks the host first and records the intention only when the host started.</summary>
    [Fact]
    public async Task Enabling_records_the_intention_only_when_the_host_started()
    {
        using var context = new DnsTestContext();
        await context.SaveSettings.HandleAsync(Settings(), CancellationToken.None);
        context.Agent.FailNext = Error.Of("AgentSystemFailure", ErrorType.Failure);

        var refused = await context.Enable.HandleAsync(new EnableDnsCommand("203.0.113.1", "tests"), CancellationToken.None);
        var enabledAfterRefusal = context.Database.DnsSettings.Single().Enabled;
        var accepted = await context.Enable.HandleAsync(new EnableDnsCommand("203.0.113.1", "tests"), CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.False(enabledAfterRefusal);
        Assert.True(accepted.IsSuccess);
        Assert.True(accepted.Value.Listening);
        Assert.True(context.Database.DnsSettings.Single().Enabled);
    }

    /// <summary>Disabling stops the host and records the intention.</summary>
    [Fact]
    public async Task Disabling_stops_the_host_and_records_it()
    {
        using var context = new DnsTestContext();
        await context.EnableDnsServerAsync();

        var result = await context.Disable.HandleAsync(new DisableDnsCommand("203.0.113.1", "tests"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, context.Agent.DisableCalls);
        Assert.False(context.Database.DnsSettings.Single().Enabled);
    }

    /// <summary>The status reads the host and reports an unreachable agent as such, not as stopped.</summary>
    [Fact]
    public async Task The_status_distinguishes_an_unreachable_agent_from_a_stopped_server()
    {
        using var context = new DnsTestContext();
        await context.EnableDnsServerAsync();

        var observed = await context.GetStatus.HandleAsync(new GetDnsStatusQuery(), CancellationToken.None);
        context.Agent.FailNext = Error.Of("AgentUnavailable", ErrorType.Unavailable);
        var unreachable = await context.GetStatus.HandleAsync(new GetDnsStatusQuery(), CancellationToken.None);

        Assert.True(observed.Value.AgentReachable);
        Assert.True(observed.Value.Enabled);
        Assert.False(unreachable.Value.AgentReachable);
        Assert.True(unreachable.Value.Enabled);
    }

    /// <summary>The settings validator refuses a bad name, a bad address and a time to live out of range.</summary>
    [Fact]
    public void The_settings_validator_refuses_bad_values()
    {
        var validator = new SaveDnsSettingsCommandValidator();

        Assert.True(validator.Validate(Settings()).IsValid);
        Assert.False(validator.Validate(Settings() with { PrimaryNameserver = "ns1\nevil.example" }).IsValid);
        Assert.False(validator.Validate(Settings() with { PublicIpv4 = "999.1.1.1" }).IsValid);
        Assert.False(validator.Validate(Settings() with { PublicIpv6 = "203.0.113.5" }).IsValid);
        Assert.False(validator.Validate(Settings() with { DefaultTtl = 5 }).IsValid);
        Assert.False(validator.Validate(Settings() with { HostmasterMailbox = string.Empty }).IsValid);
    }
}
