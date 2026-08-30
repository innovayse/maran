using Maran.Agent.Client.Services.SystemService;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.SystemService;

/// <summary>Mapping contract of AgentSystemClient (proto oneof → Result).</summary>
public sealed class AgentSystemClientTests
{
    [Fact]
    public async Task Ok_payload_maps_to_success_result()
    {
        var response = new GetAgentInfoResponse
        {
            Ok = new AgentInfo { Version = "0.1.0", DistroId = "ubuntu", Family = DistroFamily.Debian, ProtoVersion = 1 },
        };
        var client = new AgentSystemClient(new StubSystemService(response), NullLogger<AgentSystemClient>.Instance);

        var result = await client.GetInfoAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ubuntu", result.Value.DistroId);
    }

    [Fact]
    public async Task Error_payload_maps_to_failed_result_with_agent_code()
    {
        var response = new GetAgentInfoResponse
        {
            Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "boom" },
        };
        var client = new AgentSystemClient(new StubSystemService(response), NullLogger<AgentSystemClient>.Instance);

        var result = await client.GetInfoAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }

    [Fact]
    public async Task Unset_oneof_maps_to_invalid_response_error()
    {
        var response = new GetAgentInfoResponse();
        var client = new AgentSystemClient(new StubSystemService(response), NullLogger<AgentSystemClient>.Instance);

        var result = await client.GetInfoAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }
}
