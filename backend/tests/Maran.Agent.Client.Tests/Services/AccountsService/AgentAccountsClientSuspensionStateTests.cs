using Maran.Agent.Client.Services.AccountsService;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.AccountsService;

/// <summary>
/// How the host's answer about a suspension reaches the panel's own type, field by field.
/// </summary>
/// <remarks>
/// The mapping is worth its own file because of what it looks like: two counts of the same type side
/// by side, a third beside them, and two lists of pairs. Every swap in it compiles, and each one
/// produces a plausible-looking answer that the Accounts handler then refuses or accepts a suspension
/// on. Nothing else in this repository would notice.
/// </remarks>
public sealed class AgentAccountsClientSuspensionStateTests
{
    /// <summary>Every field of the answer lands in its own place.</summary>
    /// <remarks>
    /// The values are deliberately all different, and the counts deliberately not equal: a total and
    /// a suspended count that happened to match would let a swap of the two pass, and equality is
    /// exactly what the handler tests to decide whether cron is silenced.
    /// </remarks>
    [Fact]
    public async Task Every_field_of_the_hosts_answer_lands_in_its_own_place()
    {
        var stub = new StubAccountsService
        {
            SuspensionStateResponse = new GetAccountSuspensionStateResponse
            {
                Ok = new GetAccountSuspensionStateOk
                {
                    LoginLocked = true,
                    LoginPasswordState = LoginPasswordState.Absent,
                    SitesDirectoryReadable = true,
                    Sites =
                    {
                        new SiteSuspensionFact { Domain = "a.example.com", ServingStub = true },
                        new SiteSuspensionFact { Domain = "b.example.com", ServingStub = false },
                    },
                    CronEntriesTotal = 7,
                    CronEntriesSuspended = 5,
                    CronForeignLines = 3,
                    SftpLogins =
                    {
                        new SftpLoginSuspensionFact { Username = "alice_web", Locked = true },
                        new SftpLoginSuspensionFact { Username = "alice_deploy", Locked = false },
                    },
                },
            },
        };

        var result = await Client(stub).GetSuspensionStateAsync("alice", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var state = result.Value!;
        Assert.True(state.LoginLocked);
        Assert.Equal(AccountLoginPasswordState.Absent, state.LoginPasswordState);
        Assert.True(state.SitesDirectoryReadable);
        Assert.Equal(
            [("a.example.com", true), ("b.example.com", false)],
            state.Sites.Select(site => { return (site.Domain, site.ServingStub); }));
        Assert.Equal(7u, state.CronEntriesTotal);
        Assert.Equal(5u, state.CronEntriesSuspended);
        Assert.Equal(3u, state.CronForeignLines);
        Assert.Equal(
            [("alice_web", true), ("alice_deploy", false)],
            state.SftpLogins.Select(login => { return (login.Username, login.Locked); }));
    }

    /// <summary>A password state this build has never heard of reads as unspecified.</summary>
    /// <remarks>
    /// Version skew in the direction nobody arranges for: a NEWER agent naming a fifth state. The
    /// mapping is by number, so an unknown number must land on
    /// <see cref="AccountLoginPasswordState.Unspecified"/> — which sends both handlers back to
    /// <c>LoginLocked</c> and refuses — rather than on whatever the cast produced, which would be a
    /// value the handlers' switches fall through.
    /// </remarks>
    [Fact]
    public async Task A_password_state_this_build_does_not_know_reads_as_unspecified()
    {
        var stub = new StubAccountsService
        {
            SuspensionStateResponse = new GetAccountSuspensionStateResponse
            {
                Ok = new GetAccountSuspensionStateOk
                {
                    LoginPasswordState = (LoginPasswordState)99,
                },
            },
        };

        var result = await Client(stub).GetSuspensionStateAsync("alice", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountLoginPasswordState.Unspecified, result.Value!.LoginPasswordState);
    }

    /// <summary>The request carries the account the caller asked about.</summary>
    [Fact]
    public async Task The_request_carries_the_account_the_caller_asked_about()
    {
        var stub = new StubAccountsService
        {
            SuspensionStateResponse = new GetAccountSuspensionStateResponse
            {
                Ok = new GetAccountSuspensionStateOk(),
            },
        };

        await Client(stub).GetSuspensionStateAsync("alice", CancellationToken.None);

        var request = Assert.IsType<GetAccountSuspensionStateRequest>(stub.LastSuspensionStateRequest);
        Assert.Equal("alice", request.Username);
    }

    /// <summary>An error answer maps to a failed result with the agent's code.</summary>
    /// <remarks>
    /// The direction that matters: a refusal read as an empty observation would be an unreadable
    /// crontab and an unreadable password database both arriving as "nothing is running".
    /// </remarks>
    [Fact]
    public async Task An_error_answer_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubAccountsService
        {
            SuspensionStateResponse = new GetAccountSuspensionStateResponse
            {
                Error = new AgentError { Code = ErrorCode.NotFound, Message = "no such account" },
            },
        };

        var result = await Client(stub).GetSuspensionStateAsync("alice", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
    }

    /// <summary>The client under test, over the stub invoker.</summary>
    /// <param name="stub">The invoker to answer from.</param>
    /// <returns>The client.</returns>
    private static AgentAccountsClient Client(StubAccountsService stub)
    {
        return new AgentAccountsClient(stub, NullLogger<AgentAccountsClient>.Instance);
    }
}
