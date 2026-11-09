using Maran.Agent.Client.Services.MailService;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.Services.MailService;

namespace Maran.Agent.Simulated.Tests.Services.MailService;

/// <summary>What a simulated server, where mail is never enabled, answers to the mail contract.</summary>
public sealed class SimulatedAgentMailClientTests
{
    /// <summary>The server reports its mail stack as not installed rather than failing.</summary>
    [Fact]
    public async Task The_status_reports_the_stack_as_not_installed()
    {
        var client = new SimulatedAgentMailClient();

        var status = await client.GetStatusAsync(CancellationToken.None);

        Assert.True(status.IsSuccess);
        Assert.Equal(AgentMailObservation.No, status.Value.Enabled);
        Assert.Empty(status.Value.Daemons);
    }

    /// <summary>Enabling mail is refused as an operation this agent does not perform, with the real code.</summary>
    [Fact]
    public async Task Enabling_mail_is_refused_as_not_implemented()
    {
        var client = new SimulatedAgentMailClient();
        var settings = new AgentMailServerSettings("mail.example.test", false, false, 25, 4, 6, 15, false, null);

        var enabled = await client.EnableAsync(settings, CancellationToken.None);

        Assert.False(enabled.IsSuccess);
        Assert.Equal(SimulatedAgentErrors.NotImplementedCode, enabled.Error!.Code);
    }

    /// <summary>The control for the refusals above: account lifecycle calls succeed with nothing to do.</summary>
    [Fact]
    public async Task Removing_the_mail_of_an_account_with_none_succeeds_with_zeros()
    {
        var client = new SimulatedAgentMailClient();

        var removed = await client.RemoveAccountMailAsync("alice", CancellationToken.None);
        var suspended = await client.SetAccountSuspensionAsync("alice", true, CancellationToken.None);

        Assert.Equal(new AgentAccountMailRemoval(0, 0), removed.Value);
        Assert.Equal(new AgentMailSuspensionOutcome(0, 0), suspended.Value);
    }
}
