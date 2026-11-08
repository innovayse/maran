using Maran.Agent.Client.Services.AccountsService;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.AccountsService;

/// <summary>
/// How an account's quota reaches the wire (issue #194): the agent replaces blocks and inodes in one
/// call and keeps no record of the inode figure, so the client states it on every call, as a stated
/// ceiling or as an absent one, never as a zero.
/// </summary>
public sealed class AgentAccountsClientQuotaTests
{
    /// <summary>A stated inode ceiling lands beside the byte quota.</summary>
    [Fact]
    public async Task A_stated_inode_ceiling_lands_beside_the_byte_quota()
    {
        var stub = new StubAccountsService { QuotaResponse = new SetAccountQuotaResponse { Ok = new SetAccountQuotaOk() } };

        var result = await Client(stub).SetQuotaAsync("alice", 2048, 50_000, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = stub.LastQuotaRequest!;
        Assert.Equal("alice", request.Username);
        Assert.Equal(2048ul, request.QuotaBytes);
        Assert.Equal(ResourceCeiling.CeilingOneofCase.Maximum, request.InodeLimit.CeilingCase);
        Assert.Equal(50_000u, request.InodeLimit.Maximum);
    }

    /// <summary>No inode ceiling leaves the field absent, which the wire reads as unlimited.</summary>
    [Fact]
    public async Task No_inode_ceiling_leaves_the_field_absent()
    {
        var stub = new StubAccountsService { QuotaResponse = new SetAccountQuotaResponse { Ok = new SetAccountQuotaOk() } };

        var result = await Client(stub).SetQuotaAsync("alice", 2048, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(stub.LastQuotaRequest!.InodeLimit);
    }

    /// <summary>A zero inode ceiling is refused before the wire, because the agent would read it as absent.</summary>
    [Fact]
    public async Task A_zero_inode_ceiling_is_refused_before_it_reaches_the_wire()
    {
        var stub = new StubAccountsService { QuotaResponse = new SetAccountQuotaResponse { Ok = new SetAccountQuotaOk() } };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await Client(stub).SetQuotaAsync("alice", 2048, 0, CancellationToken.None);
        });
        Assert.Null(stub.LastQuotaRequest);
    }

    /// <summary>Builds the client over a stub invoker.</summary>
    /// <param name="stub">The stub that answers the rpc.</param>
    /// <returns>The client under test.</returns>
    private static AgentAccountsClient Client(StubAccountsService stub)
    {
        return new AgentAccountsClient(stub, NullLogger<AgentAccountsClient>.Instance);
    }
}
