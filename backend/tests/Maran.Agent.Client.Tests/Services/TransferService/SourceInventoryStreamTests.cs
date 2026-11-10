using Grpc.Core;
using Maran.Agent.Client.Services.TransferService;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Wire = Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.TransferService;

/// <summary>What the inventory stream becomes: typed events, and exactly one ending, whatever the agent did.</summary>
public sealed class SourceInventoryStreamTests
{
    /// <summary>How long any stream test may wait before it is a failure rather than a hang.</summary>
    private static readonly TimeSpan StreamTimeout = TimeSpan.FromSeconds(5);

    /// <summary>A healthy stream becomes progress, account, warning and done, in the agent's order.</summary>
    [Fact]
    public async Task A_complete_stream_becomes_typed_events_ending_in_done()
    {
        var stub = new StubTransferService();
        stub.InventoryResponses.Add(Progress(Wire.SourceInventoryStep.Accounts));
        stub.InventoryResponses.Add(Account("alice"));
        stub.InventoryResponses.Add(Warning(Wire.SourceWarningKind.MailNotMigrated, "alice"));
        stub.InventoryResponses.Add(Done(1, 1));

        var events = await DrainAsync(NewClient(stub));

        Assert.Equal(4, events.Count);
        Assert.Equal(SourceInventoryStep.Accounts, Assert.IsType<AgentSourceInventoryProgress>(events[0]).Step);
        var account = Assert.IsType<AgentSourceInventoryAccountFound>(events[1]).Account;
        Assert.Equal("alice", account.Name);
        Assert.Equal(120L, account.DiskBytes);
        Assert.Equal(7, account.CronEntryCount);
        var site = Assert.Single(account.Sites);
        Assert.Equal("example.test", site.Domain);
        Assert.Equal(["www.example.test"], site.Aliases);
        Assert.Equal(SourceSiteBackend.Php, site.Backend);
        Assert.Equal("8.3", site.PhpVersion);
        var database = Assert.Single(account.Databases);
        Assert.Equal("alice_shop", database.Name);
        Assert.Equal(2048L, database.SizeBytes);
        var warning = Assert.IsType<AgentSourceInventoryWarningRaised>(events[2]).Warning;
        Assert.Equal(SourceWarningKind.MailNotMigrated, warning.Kind);
        Assert.Equal("alice", warning.Subject);
        var done = Assert.IsType<AgentSourceInventoryDone>(events[3]);
        Assert.Equal((1, 1), (done.AccountCount, done.WarningCount));
    }

    /// <summary>A stream that ends with no terminal message is a failure, never a quiet completion.</summary>
    /// <remarks>
    /// The harm this prevents: an inventory missing its tail recorded as the whole server. The inverse
    /// control is the previous test, where the same messages followed by a done message end in done.
    /// </remarks>
    [Fact]
    public async Task A_stream_that_ends_without_a_terminal_message_yields_a_failed_event()
    {
        var stub = new StubTransferService();
        stub.InventoryResponses.Add(Progress(Wire.SourceInventoryStep.Accounts));
        stub.InventoryResponses.Add(Account("alice"));

        var events = await DrainAsync(NewClient(stub));

        var failed = Assert.IsType<AgentSourceInventoryFailed>(events[^1]);
        Assert.Equal("AgentInvalidResponse", failed.Error.Code);
        Assert.Equal(ErrorType.Failure, failed.Error.Type);
        Assert.DoesNotContain(events, entry => { return entry is AgentSourceInventoryDone; });
    }

    /// <summary>A stream that says nothing at all is a single failure.</summary>
    [Fact]
    public async Task An_empty_stream_is_a_single_failed_event()
    {
        var events = await DrainAsync(NewClient(new StubTransferService()));

        Assert.Equal("AgentInvalidResponse", Assert.IsType<AgentSourceInventoryFailed>(Assert.Single(events)).Error.Code);
    }

    /// <summary>The agent's own terminal error carries its code and the reason the source refused.</summary>
    [Fact]
    public async Task A_terminal_error_carries_the_code_and_the_reason()
    {
        var stub = new StubTransferService();
        stub.InventoryResponses.Add(new Wire.ReadSourceInventoryResponse
        {
            Error = new Wire.AgentError { Code = Wire.ErrorCode.ValidationFailed, Message = "REMOTE HOST IDENTIFICATION CHANGED" },
            FailureReason = Wire.SourceFailureReason.HostKeyChanged,
        });

        var events = await DrainAsync(NewClient(stub));

        var failed = Assert.IsType<AgentSourceInventoryFailed>(Assert.Single(events));
        Assert.Equal("AgentValidationFailed", failed.Error.Code);
        Assert.Equal(SourceFailureReason.HostKeyChanged, failed.Reason);
    }

    /// <summary>Nothing after the terminal message is delivered.</summary>
    [Fact]
    public async Task Nothing_after_the_terminal_message_is_delivered()
    {
        var stub = new StubTransferService();
        stub.InventoryResponses.Add(Done(0, 0));
        stub.InventoryResponses.Add(Account("late"));

        var events = await DrainAsync(NewClient(stub));

        Assert.IsType<AgentSourceInventoryDone>(Assert.Single(events));
    }

    /// <summary>A message carrying no branch is a failure and ends the stream.</summary>
    [Fact]
    public async Task A_message_with_no_branch_is_a_failure_that_ends_the_stream()
    {
        var stub = new StubTransferService();
        stub.InventoryResponses.Add(new Wire.ReadSourceInventoryResponse());
        stub.InventoryResponses.Add(Done(0, 0));

        var events = await DrainAsync(NewClient(stub));

        Assert.Equal("AgentInvalidResponse", Assert.IsType<AgentSourceInventoryFailed>(Assert.Single(events)).Error.Code);
    }

    /// <summary>A message the panel cannot read in full is a failure, never skipped.</summary>
    /// <param name="kind">Which unreadable message the agent sends.</param>
    [Theory]
    [InlineData("account-without-a-name")]
    [InlineData("site-without-a-domain")]
    [InlineData("site-backend-this-build-does-not-know")]
    [InlineData("database-without-a-name")]
    [InlineData("step-this-build-does-not-know")]
    [InlineData("step-not-a-step")]
    [InlineData("warning-not-a-warning")]
    [InlineData("warning-kind-this-build-does-not-know")]
    [InlineData("count-that-does-not-fit")]
    public async Task A_message_that_cannot_be_read_in_full_is_a_failure(string kind)
    {
        var stub = new StubTransferService();
        stub.InventoryResponses.Add(Unreadable(kind));
        stub.InventoryResponses.Add(Done(0, 0));

        var events = await DrainAsync(NewClient(stub));

        Assert.Equal("AgentInvalidResponse", Assert.IsType<AgentSourceInventoryFailed>(Assert.Single(events)).Error.Code);
    }

    /// <summary>A transport that breaks mid-stream ends it with the unavailable code, not with an exception.</summary>
    [Fact]
    public async Task A_torn_connection_ends_the_stream_in_a_failed_event()
    {
        var stub = new StubTransferService { InventoryFailure = new RpcException(new Status(StatusCode.Unavailable, "gone")) };
        stub.InventoryResponses.Add(Account("alice"));

        var events = await DrainAsync(NewClient(stub));

        Assert.IsType<AgentSourceInventoryAccountFound>(events[0]);
        var failed = Assert.IsType<AgentSourceInventoryFailed>(events[^1]);
        Assert.Equal("AgentUnavailable", failed.Error.Code);
        Assert.Equal(SourceFailureReason.Unspecified, failed.Reason);
    }

    /// <summary>A caller that cancelled is not told the agent truncated the stream.</summary>
    [Fact]
    public async Task A_cancellation_is_not_reported_as_a_truncation()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await DrainAsync(NewClient(new StubTransferService()), null, source.Token);
        });
    }

    /// <summary>The endpoint and the credential reach the wire, and the private key stays out of the log.</summary>
    [Fact]
    public async Task The_request_carries_the_endpoint_and_the_credential()
    {
        var stub = new StubTransferService();
        stub.InventoryResponses.Add(Done(0, 0));
        var credential = new AgentSourceCredential(new SensitiveString("KEY-TEXT"), "ssh-ed25519 AAAApinned");

        await DrainAsync(NewClient(stub), credential);

        Assert.Equal("203.0.113.9", stub.LastInventoryRequest!.Endpoint.Host);
        Assert.Equal(22u, stub.LastInventoryRequest.Endpoint.Port);
        Assert.Equal("root", stub.LastInventoryRequest.Endpoint.User);
        Assert.Equal("KEY-TEXT", stub.LastInventoryRequest.Credential.PrivateKey.ToStringUtf8());
        Assert.Equal("ssh-ed25519 AAAApinned", stub.LastInventoryRequest.Credential.HostKey);
    }

    /// <summary>One unreadable wire message of the kind a test names.</summary>
    /// <param name="kind">Which unreadable shape.</param>
    /// <returns>The message.</returns>
    private static Wire.ReadSourceInventoryResponse Unreadable(string kind)
    {
        switch (kind)
        {
            case "account-without-a-name":
                return new Wire.ReadSourceInventoryResponse { Account = new Wire.SourceAccount { Home = "/home/x" } };
            case "site-without-a-domain":
                return AccountWith(site: new Wire.SourceSite { Backend = Wire.SourceSiteBackend.Static });
            case "site-backend-this-build-does-not-know":
                return AccountWith(site: new Wire.SourceSite { Domain = "a.test", Backend = (Wire.SourceSiteBackend)99 });
            case "database-without-a-name":
                return AccountWith(database: new Wire.SourceDatabase { SizeKnown = true, SizeBytes = 1 });
            case "step-this-build-does-not-know":
                return Progress((Wire.SourceInventoryStep)99);
            case "step-not-a-step":
                return Progress(Wire.SourceInventoryStep.Unspecified);
            case "warning-not-a-warning":
                return Warning(Wire.SourceWarningKind.Unspecified, string.Empty);
            case "warning-kind-this-build-does-not-know":
                return Warning((Wire.SourceWarningKind)99, string.Empty);
            case "count-that-does-not-fit":
                return new Wire.ReadSourceInventoryResponse
                {
                    Done = new Wire.ReadSourceInventoryDone { AccountCount = uint.MaxValue },
                };
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a shape this test knows.");
        }
    }

    /// <summary>A progress message.</summary>
    /// <param name="step">The step it states.</param>
    /// <returns>The message.</returns>
    private static Wire.ReadSourceInventoryResponse Progress(Wire.SourceInventoryStep step)
    {
        return new Wire.ReadSourceInventoryResponse { Progress = new Wire.ReadSourceInventoryProgress { Step = step } };
    }

    /// <summary>A warning message.</summary>
    /// <param name="kind">The kind it states.</param>
    /// <param name="subject">What it concerns.</param>
    /// <returns>The message.</returns>
    private static Wire.ReadSourceInventoryResponse Warning(Wire.SourceWarningKind kind, string subject)
    {
        return new Wire.ReadSourceInventoryResponse { Warning = new Wire.SourceWarning { Kind = kind, Subject = subject } };
    }

    /// <summary>A terminal ok message.</summary>
    /// <param name="accounts">How many accounts it says were sent.</param>
    /// <param name="warnings">How many warnings it says were sent.</param>
    /// <returns>The message.</returns>
    private static Wire.ReadSourceInventoryResponse Done(uint accounts, uint warnings)
    {
        return new Wire.ReadSourceInventoryResponse
        {
            Done = new Wire.ReadSourceInventoryDone { AccountCount = accounts, WarningCount = warnings },
        };
    }

    /// <summary>A complete account message with one PHP site and one database.</summary>
    /// <param name="name">The account's system user name.</param>
    /// <returns>The message.</returns>
    private static Wire.ReadSourceInventoryResponse Account(string name)
    {
        return AccountWith(
            name,
            new Wire.SourceSite
            {
                Domain = "example.test",
                Aliases = { "www.example.test" },
                Backend = Wire.SourceSiteBackend.Php,
                PhpVersion = "8.3",
                DocumentRoot = "/home/" + name + "/public_html",
            },
            new Wire.SourceDatabase { Name = name + "_shop", SizeBytes = 2048, SizeKnown = true });
    }

    /// <summary>An account message holding at most one site and one database.</summary>
    /// <param name="name">The account's system user name.</param>
    /// <param name="site">The site it holds, or none.</param>
    /// <param name="database">The database it holds, or none.</param>
    /// <returns>The message.</returns>
    private static Wire.ReadSourceInventoryResponse AccountWith(
        string name = "alice",
        Wire.SourceSite? site = null,
        Wire.SourceDatabase? database = null)
    {
        var account = new Wire.SourceAccount
        {
            Name = name,
            Home = "/home/" + name,
            DiskBytes = 120,
            DiskKnown = true,
            CronEntryCount = 7,
            MailboxCount = 2,
        };
        if (site is not null)
        {
            account.Sites.Add(site);
        }

        if (database is not null)
        {
            account.Databases.Add(database);
        }

        return new Wire.ReadSourceInventoryResponse { Account = account };
    }

    /// <summary>Builds the client over a stub transport and a logger that discards.</summary>
    /// <param name="stub">The canned transport.</param>
    /// <returns>The client under test.</returns>
    private static AgentTransferClient NewClient(StubTransferService stub)
    {
        return new AgentTransferClient(stub, NullLogger<AgentTransferClient>.Instance);
    }

    /// <summary>Reads the whole inventory stream, failing the test instead of hanging.</summary>
    /// <param name="client">The client under test.</param>
    /// <param name="credential">The credential to read with; a default one when omitted.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>Every event, in order.</returns>
    private static async Task<List<AgentSourceInventoryEvent>> DrainAsync(
        AgentTransferClient client,
        AgentSourceCredential? credential = null,
        CancellationToken cancellationToken = default)
    {
        var endpoint = new AgentSourceEndpoint("203.0.113.9", 22, "root");
        var key = credential ?? new AgentSourceCredential(new SensitiveString("KEY"), "ssh-ed25519 AAAAx");

        async Task<List<AgentSourceInventoryEvent>> ReadAsync()
        {
            var collected = new List<AgentSourceInventoryEvent>();
            await foreach (var item in client.ReadSourceInventoryAsync(endpoint, key, cancellationToken))
            {
                collected.Add(item);
            }

            return collected;
        }

        return await ReadAsync().WaitAsync(StreamTimeout, cancellationToken);
    }
}
