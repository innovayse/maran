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
                        new SftpLoginSuspensionFact
                        {
                            Username = "alice_web",
                            Locked = true,
                            Protocol = TransferProtocol.Sftp,
                        },
                        new SftpLoginSuspensionFact
                        {
                            Username = "alice_deploy",
                            Locked = false,
                            Protocol = TransferProtocol.Ftps,
                        },
                    },
                    UnmanagedLogins = 2,
                },
            },
        };

        var result = await Client(stub).GetSuspensionStateAsync("alice", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var state = result.Value!;
        Assert.Equal(AccountLoginPasswordState.Absent, state.LoginPasswordState);
        Assert.True(state.SitesDirectoryReadable);
        Assert.Equal(
            [("a.example.com", true), ("b.example.com", false)],
            state.Sites.Select(site => { return (site.Domain, site.ServingStub); }));
        Assert.Equal(7u, state.CronEntriesTotal);
        Assert.Equal(5u, state.CronEntriesSuspended);
        Assert.Equal(3u, state.CronForeignLines);
        Assert.Equal(
            [
                ("alice_web", true, LoginTransferProtocol.Sftp),
                ("alice_deploy", false, LoginTransferProtocol.Ftps),
            ],
            state.FileTransferLogins.Select(login => { return (login.Username, login.Locked, login.Protocol); }));
        Assert.Equal(new MeasuredUnmanagedLogins(2), state.UnmanagedLogins);
    }

    /// <summary>The count of logins the suspension could not cover survives the trip to the panel.</summary>
    /// <remarks>
    /// Its own test beside the field-by-field one, because this is the number an operator is owed and
    /// the one this mapping was measured to be dropping — the panel showed the covered count and
    /// swallowed the uncovered one, which is an attestation claiming a silence it never achieved.
    ///
    /// The value is deliberately not equal to any other count in the answer, and the counts around it
    /// are populated so that a mapping reading the wrong field lands on a DIFFERENT number rather than
    /// on a coincidence. Zero as an expected value would have proved nothing here: it is also what an
    /// unread field yields.
    /// </remarks>
    [Fact]
    public async Task The_count_of_logins_the_suspension_did_not_cover_reaches_the_panel()
    {
        var stub = new StubAccountsService
        {
            SuspensionStateResponse = new GetAccountSuspensionStateResponse
            {
                Ok = new GetAccountSuspensionStateOk
                {
                    CronEntriesTotal = 7,
                    CronEntriesSuspended = 5,
                    CronForeignLines = 3,
                    UnmanagedLogins = 11,
                },
            },
        };

        var result = await Client(stub).GetSuspensionStateAsync("alice", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new MeasuredUnmanagedLogins(11), result.Value!.UnmanagedLogins);
        Assert.Equal(3u, result.Value.CronForeignLines);
    }

    /// <summary>A login the agent reported without a protocol reads as sftp.</summary>
    /// <remarks>
    /// Version skew downwards: an agent predating the field sends nothing, and the client resolves
    /// that to SFTP because before FTPS existed every reported login was an SFTP one by construction.
    /// What must never happen is the contract's zero value leaving the client, which would hand every
    /// module a protocol that is not one.
    /// </remarks>
    [Fact]
    public async Task A_login_reported_without_a_protocol_reads_as_sftp()
    {
        var stub = new StubAccountsService
        {
            SuspensionStateResponse = new GetAccountSuspensionStateResponse
            {
                Ok = new GetAccountSuspensionStateOk
                {
                    SftpLogins = { new SftpLoginSuspensionFact { Username = "alice_web", Locked = true } },
                },
            },
        };

        var result = await Client(stub).GetSuspensionStateAsync("alice", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var login = Assert.Single(result.Value!.FileTransferLogins);
        Assert.Equal(LoginTransferProtocol.Sftp, login.Protocol);

        // Positive control: the record around the protocol did carry its other fields, so the
        // answer above is the mapping's and not an empty entry.
        Assert.Equal("alice_web", login.Username);
        Assert.True(login.Locked);
    }

    /// <summary>A protocol this build has never heard of reads as sftp, and an ftps login still reads as ftps.</summary>
    /// <remarks>
    /// Version skew upwards, the same shape as the password state's: a NEWER agent naming a third
    /// daemon. The mapping is explicit, so an unknown number must land on a protocol the panel can
    /// name rather than on whatever a cast produced. The ftps login beside it is the inverse
    /// control: a mapping that answered sftp for everything would satisfy the first half alone.
    /// </remarks>
    [Fact]
    public async Task A_protocol_this_build_does_not_know_reads_as_sftp()
    {
        var stub = new StubAccountsService
        {
            SuspensionStateResponse = new GetAccountSuspensionStateResponse
            {
                Ok = new GetAccountSuspensionStateOk
                {
                    SftpLogins =
                    {
                        new SftpLoginSuspensionFact
                        {
                            Username = "alice_web",
                            Locked = true,
                            Protocol = (TransferProtocol)99,
                        },
                        new SftpLoginSuspensionFact
                        {
                            Username = "alice_files",
                            Locked = true,
                            Protocol = TransferProtocol.Ftps,
                        },
                    },
                },
            },
        };

        var result = await Client(stub).GetSuspensionStateAsync("alice", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [LoginTransferProtocol.Sftp, LoginTransferProtocol.Ftps],
            result.Value!.FileTransferLogins.Select(login => { return login.Protocol; }));
    }

    /// <summary>A password state this build has never heard of reads as the state the boolean implies.</summary>
    /// <remarks>
    /// Version skew in the direction nobody arranges for: a NEWER agent naming a fifth state. The
    /// contract's zero and an unknown number are answered from the older boolean in the one way that
    /// is conservative in both directions: a lock reads as <see cref="AccountLoginPasswordState.Locked"/>,
    /// no lock as <see cref="AccountLoginPasswordState.Usable"/>. Both rows are asserted so that a
    /// mapping answering one of them for everything fails the other.
    /// </remarks>
    [Theory]
    [InlineData(99, true, AccountLoginPasswordState.Locked)]
    [InlineData(99, false, AccountLoginPasswordState.Usable)]
    [InlineData(0, true, AccountLoginPasswordState.Locked)]
    [InlineData(0, false, AccountLoginPasswordState.Usable)]
    public async Task A_password_state_the_agent_did_not_classify_reads_as_the_state_the_boolean_implies(
        int wireState,
        bool loginLocked,
        AccountLoginPasswordState expected)
    {
        var stub = new StubAccountsService
        {
            SuspensionStateResponse = new GetAccountSuspensionStateResponse
            {
                Ok = new GetAccountSuspensionStateOk
                {
                    LoginLocked = loginLocked,
                    LoginPasswordState = (LoginPasswordState)wireState,
                },
            },
        };

        var result = await Client(stub).GetSuspensionStateAsync("alice", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value!.LoginPasswordState);
    }

    /// <summary>A classified password state is never overridden by the boolean.</summary>
    /// <remarks>
    /// The inverse control for the theory above: a login with no hash reads as locked through
    /// <c>passwd -S</c> for ever, and the classified state must win over that boolean, or the
    /// reactivation would refuse every hosting account again.
    /// </remarks>
    [Fact]
    public async Task A_classified_password_state_is_never_overridden_by_the_boolean()
    {
        var stub = new StubAccountsService
        {
            SuspensionStateResponse = new GetAccountSuspensionStateResponse
            {
                Ok = new GetAccountSuspensionStateOk
                {
                    LoginLocked = true,
                    LoginPasswordState = LoginPasswordState.Absent,
                },
            },
        };

        var result = await Client(stub).GetSuspensionStateAsync("alice", CancellationToken.None);

        Assert.Equal(AccountLoginPasswordState.Absent, result.Value!.LoginPasswordState);
    }

    /// <summary>A zero count with no stated protocol is carried as unmeasured, never as a zero.</summary>
    /// <remarks>
    /// The wire cannot tell "none exist" from "the agent predates the field", so the client decides
    /// from the rest of the answer: a count above zero, or any login naming its daemon, proves the
    /// agent counted. The three rows keep the decision from collapsing to either answer.
    /// </remarks>
    [Fact]
    public async Task A_zero_count_is_unmeasured_unless_the_answer_proves_the_agent_counted()
    {
        var silent = await StateOf(new GetAccountSuspensionStateOk
        {
            SftpLogins = { new SftpLoginSuspensionFact { Username = "alice_web", Locked = true } },
        });
        var named = await StateOf(new GetAccountSuspensionStateOk
        {
            SftpLogins =
            {
                new SftpLoginSuspensionFact { Username = "alice_web", Locked = true, Protocol = TransferProtocol.Sftp },
            },
        });
        var counted = await StateOf(new GetAccountSuspensionStateOk { UnmanagedLogins = 4 });
        var empty = await StateOf(new GetAccountSuspensionStateOk());

        Assert.Equal(new UnmeasuredUnmanagedLogins(), silent.UnmanagedLogins);
        Assert.Equal(new MeasuredUnmanagedLogins(0), named.UnmanagedLogins);
        Assert.Equal(new MeasuredUnmanagedLogins(4), counted.UnmanagedLogins);
        Assert.Equal(new UnmeasuredUnmanagedLogins(), empty.UnmanagedLogins);
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

    /// <summary>Asks the client about one wire answer and returns the mapped observation.</summary>
    /// <param name="ok">The wire payload the stub answers with.</param>
    /// <returns>The mapped observation; the call is asserted to have succeeded.</returns>
    private static async Task<AccountSuspensionStateDto> StateOf(GetAccountSuspensionStateOk ok)
    {
        var stub = new StubAccountsService
        {
            SuspensionStateResponse = new GetAccountSuspensionStateResponse { Ok = ok },
        };

        var result = await Client(stub).GetSuspensionStateAsync("alice", CancellationToken.None);

        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    /// <summary>The client under test, over the stub invoker.</summary>
    /// <param name="stub">The invoker to answer from.</param>
    /// <returns>The client.</returns>
    private static AgentAccountsClient Client(StubAccountsService stub)
    {
        return new AgentAccountsClient(stub, NullLogger<AgentAccountsClient>.Instance);
    }
}
