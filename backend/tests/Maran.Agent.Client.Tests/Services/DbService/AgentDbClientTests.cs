using Maran.Agent.Client.Services.DbService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Agent.V1;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.DbService;

/// <summary>Mapping contract of AgentDbClient (proto oneof to Result, and every request field).</summary>
public sealed class AgentDbClientTests
{
    /// <summary>The password a creation call carries, long enough to be a real generated one.</summary>
    private const string GeneratedPassword = "Tz7-quiet-mule-42";

    /// <summary>Creation ok payload maps to the two fully qualified names the agent produced.</summary>
    [Fact]
    public async Task Creation_ok_payload_maps_to_the_two_fully_qualified_names_the_agent_produced()
    {
        var stub = new StubDbService
        {
            CreateResponse = new CreateDatabaseResponse
            {
                Ok = new CreateDatabaseOk { DatabaseName = "acc1_shop", DbUsername = "acc1_shopuser" },
            },
        };

        var result = await CreateAsync(stub, NullLogger<AgentDbClient>.Instance);

        Assert.True(result.IsSuccess);
        Assert.Equal("acc1_shop", result.Value.DatabaseName);
        Assert.Equal("acc1_shopuser", result.Value.DbUsername);
    }

    /// <summary>Creation sends the account the database the user and the password.</summary>
    [Fact]
    public async Task Creation_sends_the_account_the_database_the_user_and_the_password()
    {
        var stub = new StubDbService();

        await CreateAsync(stub, NullLogger<AgentDbClient>.Instance);

        var request = Assert.IsType<CreateDatabaseRequest>(stub.LastCreateRequest);
        Assert.Equal("alice", request.AccountUsername);
        Assert.Equal("shop", request.DatabaseName);
        Assert.Equal("shopuser", request.DbUsername);
        Assert.Equal(GeneratedPassword, request.Password);
    }

    /// <summary>Creation error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task Creation_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = StubDbService.FailingCreateWith(ErrorCode.AlreadyExists, "database exists");

        var result = await CreateAsync(stub, NullLogger<AgentDbClient>.Instance);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentAlreadyExists", result.Error!.Code);
    }

    /// <summary>A creation response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task A_creation_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await CreateAsync(new StubDbService(), NullLogger<AgentDbClient>.Instance);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>The generated password is passed to the agent and never written to the log.</summary>
    /// <remarks>
    /// The realistic leak is not a "using password: YES" line the panel writes about itself; it is
    /// the SERVER quoting the credential it refused, which reaches this client inside the agent's own
    /// error text and would be logged verbatim. The translator's pattern rules cannot help: a
    /// password is neither a PEM block nor a long base64 run — it is a short mixed-character string
    /// indistinguishable from a table name. What makes it findable is that the panel minted it, so it
    /// can strip the exact value it just sent.
    /// </remarks>
    [Fact]
    public async Task The_generated_password_is_passed_to_the_agent_and_never_written_to_the_log()
    {
        var logger = new RecordingLogger<AgentDbClient>();
        var stub = StubDbService.FailingCreateWith(
            ErrorCode.SystemFailure,
            $"Access denied for 'alice_shopuser'@'localhost' (using password: '{GeneratedPassword}')");

        var result = await CreateAsync(stub, logger);

        Assert.Equal(GeneratedPassword, stub.LastCreateRequest!.Password);
        Assert.False(result.IsSuccess);
        var logged = Assert.Single(logger.Messages);
        Assert.DoesNotContain(GeneratedPassword, logged, StringComparison.Ordinal);
        Assert.Contains("Access denied for 'alice_shopuser'@'localhost'", logged, StringComparison.Ordinal);
    }

    /// <summary>The agents diagnostic text is logged and never carried back to the caller.</summary>
    [Fact]
    public async Task The_agents_diagnostic_text_is_logged_and_never_carried_back_to_the_caller()
    {
        var logger = new RecordingLogger<AgentDbClient>();
        var stub = StubDbService.FailingCreateWith(
            ErrorCode.SystemFailure,
            "cannot connect to /var/run/mysqld/mysqld.sock");

        var result = await CreateAsync(stub, logger);

        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.DoesNotContain("/var/run/mysqld", result.Error.Code, StringComparison.Ordinal);
        var logged = Assert.Single(logger.Messages);
        Assert.Contains("/var/run/mysqld/mysqld.sock", logged, StringComparison.Ordinal);
    }

    /// <summary>Drop sends the account the database and the dedicated user.</summary>
    /// <remarks>
    /// All three, because the user is not derivable from the database name: the customer names the
    /// two halves independently, so a drop missing the user leaves a live credential behind.
    /// </remarks>
    [Fact]
    public async Task Drop_sends_the_account_the_database_and_the_dedicated_user()
    {
        var stub = new StubDbService
        {
            DropResponse = new DropDatabaseResponse { Ok = new DropDatabaseOk() },
        };

        await new AgentDbClient(stub, NullLogger<AgentDbClient>.Instance)
            .DropAsync("alice", "shop", "shopuser", CancellationToken.None);

        var request = Assert.IsType<DropDatabaseRequest>(stub.LastDropRequest);
        Assert.Equal("alice", request.AccountUsername);
        Assert.Equal("shop", request.DatabaseName);
        Assert.Equal("shopuser", request.DbUsername);
    }

    /// <summary>Drop ok payload maps to success.</summary>
    [Fact]
    public async Task Drop_ok_payload_maps_to_success()
    {
        var stub = new StubDbService
        {
            DropResponse = new DropDatabaseResponse { Ok = new DropDatabaseOk() },
        };

        var result = await new AgentDbClient(stub, NullLogger<AgentDbClient>.Instance)
            .DropAsync("alice", "shop", "shopuser", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    /// <summary>Drop error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task Drop_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubDbService
        {
            DropResponse = new DropDatabaseResponse
            {
                Error = new AgentError { Code = ErrorCode.NotFound, Message = "no such database" },
            },
        };

        var result = await new AgentDbClient(stub, NullLogger<AgentDbClient>.Instance)
            .DropAsync("alice", "shop", "shopuser", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
    }

    /// <summary>A drop response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task A_drop_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await new AgentDbClient(new StubDbService(), NullLogger<AgentDbClient>.Instance)
            .DropAsync("alice", "shop", "shopuser", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Listing sends the account whose databases are wanted.</summary>
    [Fact]
    public async Task Listing_sends_the_account_whose_databases_are_wanted()
    {
        var stub = new StubDbService
        {
            ListResponse = new ListDatabasesResponse { Ok = new ListDatabasesOk() },
        };

        await new AgentDbClient(stub, NullLogger<AgentDbClient>.Instance)
            .ListAsync("alice", CancellationToken.None);

        var request = Assert.IsType<ListDatabasesRequest>(stub.LastListRequest);
        Assert.Equal("alice", request.AccountUsername);
    }

    /// <summary>Fields the agent left unset arrive as null and never as an empty name or a zero size.</summary>
    /// <remarks>
    /// The agent deliberately establishes neither the dedicated user nor the size in a listing, and
    /// says so by leaving both unset. Reading the proto3 defaults instead would turn "not known" into
    /// two claims it never made: that the database has a user with no name, and that it is empty.
    /// </remarks>
    [Fact]
    public async Task Fields_the_agent_left_unset_arrive_as_null_and_never_as_an_empty_name_or_a_zero_size()
    {
        var stub = new StubDbService
        {
            ListResponse = new ListDatabasesResponse
            {
                Ok = new ListDatabasesOk
                {
                    Databases = { new DatabaseInfo { DatabaseName = "acc1_shop" } },
                },
            },
        };

        var result = await new AgentDbClient(stub, NullLogger<AgentDbClient>.Instance)
            .ListAsync("alice", CancellationToken.None);

        var row = Assert.Single(result.Value);
        Assert.Equal("acc1_shop", row.DatabaseName);
        Assert.Null(row.DbUsername);
        Assert.Null(row.SizeBytes);
    }

    /// <summary>A sender that did establish the user and the size has both carried through.</summary>
    [Fact]
    public async Task A_sender_that_did_establish_the_user_and_the_size_has_both_carried_through()
    {
        var stub = new StubDbService
        {
            ListResponse = new ListDatabasesResponse
            {
                Ok = new ListDatabasesOk
                {
                    Databases =
                    {
                        new DatabaseInfo
                        {
                            DatabaseName = "acc1_shop",
                            DbUsername = "acc1_shopuser",
                            SizeBytes = 4096,
                        },
                        new DatabaseInfo { DatabaseName = "acc1_blog" },
                    },
                },
            },
        };

        var result = await new AgentDbClient(stub, NullLogger<AgentDbClient>.Instance)
            .ListAsync("alice", CancellationToken.None);

        Assert.Equal(2, result.Value.Count);
        Assert.Equal("acc1_shop", result.Value[0].DatabaseName);
        Assert.Equal("acc1_shopuser", result.Value[0].DbUsername);
        Assert.Equal(4096UL, result.Value[0].SizeBytes);
        Assert.Equal("acc1_blog", result.Value[1].DatabaseName);
        Assert.Null(result.Value[1].DbUsername);
    }

    /// <summary>Listing error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task Listing_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubDbService
        {
            ListResponse = new ListDatabasesResponse
            {
                Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "server is down" },
            },
        };

        var result = await new AgentDbClient(stub, NullLogger<AgentDbClient>.Instance)
            .ListAsync("alice", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }

    /// <summary>A listing response with neither branch set is refused rather than read as empty.</summary>
    [Fact]
    public async Task A_listing_response_with_neither_branch_set_is_refused_rather_than_read_as_empty()
    {
        var result = await new AgentDbClient(new StubDbService(), NullLogger<AgentDbClient>.Instance)
            .ListAsync("alice", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>The size request sends the account and the database suffix.</summary>
    [Fact]
    public async Task The_size_request_sends_the_account_and_the_database_suffix()
    {
        var stub = new StubDbService
        {
            SizeResponse = new GetDatabaseSizeResponse { Ok = new GetDatabaseSizeOk() },
        };

        await new AgentDbClient(stub, NullLogger<AgentDbClient>.Instance)
            .GetSizeAsync("alice", "shop", CancellationToken.None);

        var request = Assert.IsType<GetDatabaseSizeRequest>(stub.LastSizeRequest);
        Assert.Equal("alice", request.AccountUsername);
        Assert.Equal("shop", request.DatabaseName);
    }

    /// <summary>The size ok payload maps to the measured byte count.</summary>
    [Fact]
    public async Task The_size_ok_payload_maps_to_the_measured_byte_count()
    {
        var stub = new StubDbService
        {
            SizeResponse = new GetDatabaseSizeResponse
            {
                Ok = new GetDatabaseSizeOk { SizeBytes = 12_582_912 },
            },
        };

        var result = await new AgentDbClient(stub, NullLogger<AgentDbClient>.Instance)
            .GetSizeAsync("alice", "shop", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(12_582_912UL, result.Value);
    }

    /// <summary>The size error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task The_size_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubDbService
        {
            SizeResponse = new GetDatabaseSizeResponse
            {
                Error = new AgentError { Code = ErrorCode.NotFound, Message = "no such database" },
            },
        };

        var result = await new AgentDbClient(stub, NullLogger<AgentDbClient>.Instance)
            .GetSizeAsync("alice", "shop", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
    }

    /// <summary>A size response with neither branch set is refused rather than read as zero.</summary>
    [Fact]
    public async Task A_size_response_with_neither_branch_set_is_refused_rather_than_read_as_zero()
    {
        var result = await new AgentDbClient(new StubDbService(), NullLogger<AgentDbClient>.Instance)
            .GetSizeAsync("alice", "shop", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Setting a password sends the account the user and the new value and no database.</summary>
    [Fact]
    public async Task Setting_a_password_sends_the_account_the_user_and_the_new_value_and_no_database()
    {
        var stub = new StubDbService();

        await SetPasswordAsync(stub, NullLogger<AgentDbClient>.Instance);

        var request = Assert.IsType<SetDatabasePasswordRequest>(stub.LastSetPasswordRequest);
        Assert.Equal("alice", request.AccountUsername);
        Assert.Equal("shopuser", request.DbUsername);
        Assert.Equal(GeneratedPassword, request.Password);
    }

    /// <summary>The password set ok payload maps to success.</summary>
    [Fact]
    public async Task The_password_set_ok_payload_maps_to_success()
    {
        var stub = new StubDbService
        {
            SetPasswordResponse = new SetDatabasePasswordResponse { Ok = new SetDatabasePasswordOk() },
        };

        var result = await SetPasswordAsync(stub, NullLogger<AgentDbClient>.Instance);

        Assert.True(result.IsSuccess);
    }

    /// <summary>The password set error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task The_password_set_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = StubDbService.FailingSetPasswordWith(ErrorCode.NotFound, "no such user");

        var result = await SetPasswordAsync(stub, NullLogger<AgentDbClient>.Instance);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
    }

    /// <summary>A password set response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task A_password_set_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        // The worst possible default for this call: reading an empty response as success tells the
        // customer a password they cannot use is now live, and the old one still is.
        var result = await SetPasswordAsync(new StubDbService(), NullLogger<AgentDbClient>.Instance);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>The replacement password is stripped from the agents text before it is logged.</summary>
    [Fact]
    public async Task The_replacement_password_is_stripped_from_the_agents_text_before_it_is_logged()
    {
        // Same leak as the creation path, and it must be closed on both: the server's natural way of
        // reporting a refused credential is to quote it back.
        var logger = new RecordingLogger<AgentDbClient>();
        var stub = StubDbService.FailingSetPasswordWith(
            ErrorCode.SystemFailure,
            $"ALTER USER failed for 'alice_shopuser'@'localhost' IDENTIFIED BY '{GeneratedPassword}'");

        var result = await SetPasswordAsync(stub, logger);

        Assert.False(result.IsSuccess);
        var logged = Assert.Single(logger.Messages);
        Assert.DoesNotContain(GeneratedPassword, logged, StringComparison.Ordinal);
        Assert.Contains("ALTER USER failed for 'alice_shopuser'@'localhost'", logged, StringComparison.Ordinal);
    }

    /// <summary>Calls the production password set path with fixed arguments.</summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <param name="logger">The logger the client writes the agent's text to.</param>
    /// <returns>What the client returned.</returns>
    private static async Task<SharedKernel.Results.Result<bool>> SetPasswordAsync(
        StubDbService stub,
        Microsoft.Extensions.Logging.ILogger<AgentDbClient> logger)
    {
        var client = new AgentDbClient(stub, logger);

        return await client.SetPasswordAsync(
            "alice",
            "shopuser",
            new SensitiveString(GeneratedPassword),
            CancellationToken.None);
    }

    /// <summary>Calls the production creation path with fixed arguments.</summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <param name="logger">The logger the client writes the agent's text to.</param>
    /// <returns>What the client returned.</returns>
    private static async Task<SharedKernel.Results.Result<CreatedDatabaseDto>> CreateAsync(
        StubDbService stub,
        Microsoft.Extensions.Logging.ILogger<AgentDbClient> logger)
    {
        var client = new AgentDbClient(stub, logger);

        return await client.CreateAsync(
            "alice",
            "shop",
            "shopuser",
            new SensitiveString(GeneratedPassword),
            CancellationToken.None);
    }
}
