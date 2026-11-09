using Maran.Agent.Client.Services.AccountsService;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.AccountsService;

/// <summary>
/// How the options of a new account reach the wire: the inode ceiling as a stated ceiling or as an
/// absent one, the login shell as a real value and never the contract's zero, and the open-files
/// ceiling with its removal stated rather than read off a zero.
/// </summary>
public sealed class AgentAccountsClientProvisioningTests
{
    /// <summary>A limited inode ceiling and the bash shell land in their own fields.</summary>
    [Fact]
    public async Task A_limited_inode_ceiling_and_the_bash_shell_land_in_their_own_fields()
    {
        var stub = new StubAccountsService { CreateResponse = CreatedOk() };

        var result = await Client(stub).CreateAsync(
            "alice",
            1024,
            new AccountProvisioningDto(250_000, AccountLoginShell.Bash),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = stub.LastCreateRequest!;
        Assert.Equal("alice", request.Username);
        Assert.Equal(1024ul, request.QuotaBytes);
        Assert.Equal(LoginShell.Bash, request.LoginShell);
        Assert.Equal(ResourceCeiling.CeilingOneofCase.Maximum, request.InodeLimit.CeilingCase);
        Assert.Equal(250_000u, request.InodeLimit.Maximum);
    }

    /// <summary>No inode ceiling leaves the field absent, which the wire reads as unlimited, and nologin is stated.</summary>
    [Fact]
    public async Task No_inode_ceiling_leaves_the_field_absent_and_nologin_is_stated()
    {
        var stub = new StubAccountsService { CreateResponse = CreatedOk() };

        var result = await Client(stub).CreateAsync(
            "alice",
            0,
            new AccountProvisioningDto(null, AccountLoginShell.Nologin),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(stub.LastCreateRequest!.InodeLimit);
        Assert.Equal(LoginShell.Nologin, stub.LastCreateRequest.LoginShell);
    }

    /// <summary>A created account reports where it lives and its uid.</summary>
    [Fact]
    public async Task A_created_account_reports_its_home_and_uid()
    {
        var stub = new StubAccountsService { CreateResponse = CreatedOk() };

        var result = await Client(stub).CreateAsync(
            "alice",
            0,
            new AccountProvisioningDto(null, AccountLoginShell.Nologin),
            CancellationToken.None);

        Assert.Equal("/home/alice", result.Value.HomeDirectory);
        Assert.Equal(1001u, result.Value.Uid);
    }

    /// <summary>A zero inode ceiling is refused before the wire, because the agent would read it as absent.</summary>
    [Fact]
    public async Task A_zero_inode_ceiling_is_refused_before_it_reaches_the_wire()
    {
        var stub = new StubAccountsService { CreateResponse = CreatedOk() };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await Client(stub).CreateAsync(
                "alice",
                0,
                new AccountProvisioningDto(0, AccountLoginShell.Nologin),
                CancellationToken.None);
        });
        Assert.Null(stub.LastCreateRequest);
    }

    /// <summary>An inode ceiling above the wire's range is refused rather than wrapped into a smaller one.</summary>
    [Fact]
    public async Task An_inode_ceiling_above_the_wire_range_is_refused_rather_than_wrapped()
    {
        var stub = new StubAccountsService { CreateResponse = CreatedOk() };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await Client(stub).CreateAsync(
                "alice",
                0,
                new AccountProvisioningDto((ulong)uint.MaxValue + 1, AccountLoginShell.Nologin),
                CancellationToken.None);
        });
        Assert.Null(stub.LastCreateRequest);
    }

    /// <summary>The agent's refusal of a creation comes back as a typed failure.</summary>
    [Fact]
    public async Task The_agents_refusal_of_a_creation_comes_back_as_a_typed_failure()
    {
        var stub = new StubAccountsService
        {
            CreateResponse = new CreateAccountResponse
            {
                Error = new AgentError { Code = ErrorCode.AlreadyExists, Message = "user 'alice' exists" },
            },
        };

        var result = await Client(stub).CreateAsync(
            "alice",
            0,
            new AccountProvisioningDto(null, AccountLoginShell.Nologin),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    /// <summary>An unsuspension states the shell the account is given back.</summary>
    [Theory]
    [InlineData(AccountLoginShell.Nologin, LoginShell.Nologin)]
    [InlineData(AccountLoginShell.Bash, LoginShell.Bash)]
    public async Task An_unsuspension_states_the_shell_the_account_is_given_back(
        AccountLoginShell shell,
        LoginShell expected)
    {
        var stub = new StubAccountsService { UnsuspendResponse = new UnsuspendAccountResponse { Ok = new UnsuspendAccountOk() } };

        var result = await Client(stub).UnsuspendAsync("alice", shell, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("alice", stub.LastUnsuspendRequest!.Username);
        Assert.Equal(expected, stub.LastUnsuspendRequest.LoginShell);
    }

    /// <summary>A figure for open files lands as a limited ceiling.</summary>
    [Fact]
    public async Task An_open_files_figure_lands_as_a_limited_ceiling()
    {
        var stub = new StubAccountsService
        {
            OpenFilesLimitResponse = new SetAccountOpenFilesLimitResponse { Ok = new SetAccountOpenFilesLimitOk() },
        };

        var result = await Client(stub).SetOpenFilesLimitAsync("alice", 4096, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = stub.LastOpenFilesLimitRequest!;
        Assert.Equal("alice", request.Username);
        Assert.Equal(ResourceCeiling.CeilingOneofCase.Maximum, request.OpenFiles.CeilingCase);
        Assert.Equal(4096u, request.OpenFiles.Maximum);
    }

    /// <summary>Removing the open-files limit is stated as the unlimited case, never as an absent ceiling.</summary>
    [Fact]
    public async Task Removing_the_open_files_limit_is_stated_as_the_unlimited_case()
    {
        var stub = new StubAccountsService
        {
            OpenFilesLimitResponse = new SetAccountOpenFilesLimitResponse { Ok = new SetAccountOpenFilesLimitOk() },
        };

        var result = await Client(stub).SetOpenFilesLimitAsync("alice", null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ResourceCeiling.CeilingOneofCase.Unlimited, stub.LastOpenFilesLimitRequest!.OpenFiles.CeilingCase);
    }

    /// <summary>The agent's refusal of an open-files change comes back as a typed failure.</summary>
    [Fact]
    public async Task The_agents_refusal_of_an_open_files_change_comes_back_as_a_typed_failure()
    {
        var stub = new StubAccountsService
        {
            OpenFilesLimitResponse = new SetAccountOpenFilesLimitResponse
            {
                Error = new AgentError { Code = ErrorCode.NotFound, Message = "account 'alice' was not found" },
            },
        };

        var result = await Client(stub).SetOpenFilesLimitAsync("alice", 4096, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    /// <summary>A response that states neither branch is an invalid response, not a success.</summary>
    [Fact]
    public async Task A_response_stating_neither_branch_is_an_invalid_response()
    {
        var stub = new StubAccountsService();

        var result = await Client(stub).SetOpenFilesLimitAsync("alice", 4096, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Builds a successful creation answer.</summary>
    /// <returns>The wire response.</returns>
    private static CreateAccountResponse CreatedOk()
    {
        return new CreateAccountResponse { Ok = new CreateAccountOk { HomeDirectory = "/home/alice", Uid = 1001 } };
    }

    /// <summary>Builds the client under test over the stub.</summary>
    /// <param name="stub">The transport stub.</param>
    /// <returns>The client.</returns>
    private static AgentAccountsClient Client(StubAccountsService stub)
    {
        return new AgentAccountsClient(stub, NullLogger<AgentAccountsClient>.Instance);
    }
}
