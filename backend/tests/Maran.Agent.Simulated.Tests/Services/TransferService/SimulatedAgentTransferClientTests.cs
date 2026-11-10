using Maran.Agent.Client.Services.TransferService;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.Services.TransferService;
using Maran.SharedKernel.Security;

namespace Maran.Agent.Simulated.Tests.Services.TransferService;

/// <summary>What a simulated server, which reaches no other host, answers to the transfer contract.</summary>
public sealed class SimulatedAgentTransferClientTests
{
    /// <summary>Every unary call is refused as an operation this agent does not perform, with the real code.</summary>
    [Fact]
    public async Task Every_unary_call_is_refused_as_not_implemented()
    {
        var client = new SimulatedAgentTransferClient();
        var endpoint = new AgentSourceEndpoint("203.0.113.9", 22, "root");
        var credential = new AgentSourceCredential(new SensitiveString("KEY"), "ssh-ed25519 AAAAx");

        var generated = await client.GenerateSourceKeyAsync(CancellationToken.None);
        var scanned = await client.ScanHostKeyAsync(endpoint, CancellationToken.None);
        var tested = await client.TestSourceAsync(endpoint, credential, CancellationToken.None);

        Assert.False(generated.IsSuccess);
        Assert.False(scanned.IsSuccess);
        Assert.False(tested.IsSuccess);
        Assert.Equal(SimulatedAgentErrors.NotImplementedCode, generated.Error!.Code);
        Assert.Equal(SimulatedAgentErrors.NotImplementedCode, scanned.Error!.Code);
        Assert.Equal(SimulatedAgentErrors.NotImplementedCode, tested.Error!.Code);
        Assert.Equal(SourceFailureReason.Unspecified, tested.Reason);
    }

    /// <summary>The inventory stream ends in its one failed ending and nothing else.</summary>
    [Fact]
    public async Task The_inventory_stream_is_a_single_failed_ending()
    {
        var client = new SimulatedAgentTransferClient();
        var endpoint = new AgentSourceEndpoint("203.0.113.9", 22, "root");
        var credential = new AgentSourceCredential(new SensitiveString("KEY"), "ssh-ed25519 AAAAx");
        var events = new List<AgentSourceInventoryEvent>();

        await foreach (var item in client.ReadSourceInventoryAsync(endpoint, credential, CancellationToken.None))
        {
            events.Add(item);
        }

        var failed = Assert.IsType<AgentSourceInventoryFailed>(Assert.Single(events));
        Assert.Equal(SimulatedAgentErrors.NotImplementedCode, failed.Error.Code);
    }

    /// <summary>A cancelled caller is not answered with a refusal.</summary>
    /// <remarks>The inverse control: the same call with a live token is answered, with a cancelled one it throws.</remarks>
    [Fact]
    public async Task A_cancelled_call_throws_instead_of_answering()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await new SimulatedAgentTransferClient().GenerateSourceKeyAsync(source.Token);
        });
    }
}
