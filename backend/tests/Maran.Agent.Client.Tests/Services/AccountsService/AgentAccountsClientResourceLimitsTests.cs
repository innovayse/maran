using Maran.Agent.Client.Services.AccountsService;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.AccountsService;

/// <summary>
/// How an account's runtime ceilings reach the wire: each in its own field, each with exactly one case
/// set, so the agent never reads "no ceiling" off a zero.
/// </summary>
public sealed class AgentAccountsClientResourceLimitsTests
{
    /// <summary>Each ceiling lands in its own field, a limited one as its figure and an unlimited one as its own case.</summary>
    [Fact]
    public async Task Each_ceiling_lands_in_its_own_field_with_exactly_one_case_set()
    {
        var stub = new StubAccountsService
        {
            ResourceLimitsResponse = new SetAccountResourceLimitsResponse { Ok = new SetAccountResourceLimitsOk() },
        };
        var limits = new AccountSliceLimitsDto(SliceCeiling.Limited(150), SliceCeiling.Unlimited(), SliceCeiling.Limited(64));

        var result = await Client(stub).SetResourceLimitsAsync("alice", limits, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = stub.LastResourceLimitsRequest!;
        Assert.Equal("alice", request.Username);
        Assert.Equal(ResourceCeiling.CeilingOneofCase.Maximum, request.CpuQuotaPercent.CeilingCase);
        Assert.Equal(150u, request.CpuQuotaPercent.Maximum);
        Assert.Equal(ResourceCeiling.CeilingOneofCase.Unlimited, request.MemoryMaxMib.CeilingCase);
        Assert.Equal(ResourceCeiling.CeilingOneofCase.Maximum, request.TasksMax.CeilingCase);
        Assert.Equal(64u, request.TasksMax.Maximum);
    }

    /// <summary>The agent's own refusal comes back as a typed failure carrying its code.</summary>
    [Fact]
    public async Task The_agents_refusal_comes_back_as_a_typed_failure()
    {
        var stub = new StubAccountsService
        {
            ResourceLimitsResponse = new SetAccountResourceLimitsResponse
            {
                Error = new AgentError { Code = ErrorCode.NotFound, Message = "account 'alice' was not found" },
            },
        };
        var limits = new AccountSliceLimitsDto(SliceCeiling.Unlimited(), SliceCeiling.Unlimited(), SliceCeiling.Unlimited());

        var result = await Client(stub).SetResourceLimitsAsync("alice", limits, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    /// <summary>A limited ceiling of zero cannot be built, because the agent would refuse it.</summary>
    [Fact]
    public void A_limited_ceiling_of_zero_cannot_be_built()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            SliceCeiling.Limited(0);
        });
    }

    /// <summary>Builds the client under test over the stub.</summary>
    /// <param name="stub">The transport stub.</param>
    /// <returns>The client.</returns>
    private static AgentAccountsClient Client(StubAccountsService stub)
    {
        return new AgentAccountsClient(stub, NullLogger<AgentAccountsClient>.Instance);
    }
}
