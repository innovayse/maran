using Google.Protobuf;
using Maran.Agent.Client.Services.BackupService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.BackupService;

/// <summary>Mapping contract of AgentBackupClient (proto oneof → Result, and stream → typed events).</summary>
public sealed class AgentBackupClientTests
{
    /// <summary>The secret access key the S3 destinations in these tests carry.</summary>
    private const string SecretKey = "wJalrXUtnFEMIK7MDENGbPxRfiCYEXAMPLEKEY";

    /// <summary>How long any stream test may wait before it is a failure rather than a hang.</summary>
    private static readonly TimeSpan StreamTimeout = TimeSpan.FromSeconds(5);

    /// <summary>A readable listing maps to a summary carrying the manifest.</summary>
    [Fact]
    public async Task A_readable_listing_maps_to_a_summary_carrying_the_manifest()
    {
        var stub = new StubBackupService { ListResponse = new ListBackupsResponse { Ok = ReadableListing() } };

        var result = await NewClient(stub).ListAsync("alice", Local(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var summary = Assert.Single(result.Value);
        Assert.Equal("b1", summary.BackupId);
        Assert.Null(summary.Unreadable);
        Assert.NotNull(summary.Readable);
        Assert.Equal("alice", summary.Readable.Manifest.Account);
        Assert.Equal("abc123", summary.Readable.ArtifactSha256);
        Assert.Equal(4096ul, summary.Readable.ArtifactBytes);
        var database = Assert.Single(summary.Readable.Manifest.Databases);
        Assert.Equal("alice_shop", database.Name);
    }

    /// <summary>A readable arm carrying no manifest is refused.</summary>
    /// <remarks>
    /// The item this whole client exists to get right. proto3's oneof makes the ARM exactly-one; it
    /// does NOT make <c>ReadableBackup.manifest</c> present. The agent's own summary is a Rust sum
    /// type, so it cannot construct this shape and two of its tests exhibit that — but the wire
    /// permits it, and a reader that believed the arm would hand the panel a backup nobody described
    /// and let a restore be offered against it. So the whole listing fails as an invalid response.
    /// Built here as bytes on the wire rather than as an object graph, because a hand-built message
    /// could not prove the shape survives a serialization round trip the way a real response does.
    /// </remarks>
    [Fact]
    public async Task A_readable_arm_carrying_no_manifest_is_refused()
    {
        var onTheWire = new ListBackupsResponse { Ok = ReadableListing() };
        onTheWire.Ok.Backups[0].Readable.Manifest = null;
        var received = ListBackupsResponse.Parser.ParseFrom(onTheWire.ToByteArray());

        // The hazard is actually present in what the client will read: without this, a proto that
        // refused to drop the manifest would leave the assertion below passing for the wrong reason.
        Assert.Equal(BackupInfo.StateOneofCase.Readable, received.Ok.Backups[0].StateCase);
        Assert.Null(received.Ok.Backups[0].Readable.Manifest);

        var result = await NewClient(new StubBackupService { ListResponse = received })
            .ListAsync("alice", Local(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>An entry with no state arm at all is refused.</summary>
    [Fact]
    public async Task An_entry_with_no_state_arm_at_all_is_refused()
    {
        var ok = new ListBackupsOk();
        ok.Backups.Add(new BackupInfo { BackupId = "b1" });

        var result = await NewClient(new StubBackupService { ListResponse = new ListBackupsResponse { Ok = ok } })
            .ListAsync("alice", Local(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>An unreadable entry is listed with its reason rather than dropped.</summary>
    /// <remarks>
    /// An entry nobody lists is an entry retention will never prune. The version is carried through
    /// as well, because an operator's next action differs between a damaged sidecar and a skew.
    /// </remarks>
    [Fact]
    public async Task An_unreadable_entry_is_listed_with_its_reason_rather_than_dropped()
    {
        var ok = new ListBackupsOk();
        ok.Backups.Add(new BackupInfo
        {
            BackupId = "b2",
            Unreadable = new UnreadableReason { Kind = UnreadableKind.UnknownVersion, Version = 9 },
        });

        var result = await NewClient(new StubBackupService { ListResponse = new ListBackupsResponse { Ok = ok } })
            .ListAsync("alice", Local(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var summary = Assert.Single(result.Value);
        Assert.Null(summary.Readable);
        Assert.Equal(AgentBackupUnreadableKind.UnknownVersion, summary.Unreadable!.Kind);
        Assert.Equal(9u, summary.Unreadable.Version);
    }

    /// <summary>An unreadable kind this build does not know stays unknown.</summary>
    [Fact]
    public async Task An_unreadable_kind_this_build_does_not_know_stays_unknown()
    {
        var ok = new ListBackupsOk();
        ok.Backups.Add(new BackupInfo
        {
            BackupId = "b3",
            Unreadable = new UnreadableReason { Kind = (UnreadableKind)77 },
        });

        var result = await NewClient(new StubBackupService { ListResponse = new ListBackupsResponse { Ok = ok } })
            .ListAsync("alice", Local(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentBackupUnreadableKind.Unknown, Assert.Single(result.Value).Unreadable!.Kind);
    }

    /// <summary>Listing a remote destination is refused without asking the agent.</summary>
    /// <remarks>
    /// The listing rpc carries no destination — the agent lists its own root-only directory — so
    /// asking it about a bucket would return the LOCAL backups under a caller's belief that it was
    /// reading the bucket. That silent substitution is the one outcome worse than a refusal.
    /// </remarks>
    [Fact]
    public async Task Listing_a_remote_destination_is_refused_without_asking_the_agent()
    {
        var stub = new StubBackupService { ListResponse = new ListBackupsResponse { Ok = ReadableListing() } };

        var result = await NewClient(stub).ListAsync("alice", S3(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotImplemented", result.Error!.Code);
        Assert.Null(stub.LastListRequest);
    }

    /// <summary>Listing a local destination does reach the agent.</summary>
    /// <remarks>
    /// The inverse control for the refusal above: a gate that refused everything would pass that
    /// test and be useless, so this one proves the same code path accepts the case it must accept.
    /// </remarks>
    [Fact]
    public async Task Listing_a_local_destination_does_reach_the_agent()
    {
        var stub = new StubBackupService { ListResponse = new ListBackupsResponse { Ok = ReadableListing() } };

        var result = await NewClient(stub).ListAsync("alice", Local(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("alice", stub.LastListRequest!.AccountUsername);
    }

    /// <summary>A not implemented refusal maps to its own code.</summary>
    /// <remarks>
    /// The agent refuses an object-store destination at its service boundary. Before this code
    /// existed the refusal fell to the unspecified arm and told an operator that something had gone
    /// wrong on their server, about a feature that was never built.
    /// </remarks>
    [Fact]
    public async Task A_not_implemented_refusal_maps_to_its_own_code()
    {
        var stub = new StubBackupService
        {
            DeleteResponse = new DeleteBackupResponse
            {
                Error = new AgentError { Code = ErrorCode.NotImplemented, Message = "s3 destinations are refused" },
            },
        };

        var result = await NewClient(stub).DeleteAsync("alice", "b1", S3(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotImplemented", result.Error!.Code);
    }

    /// <summary>An error payload maps to a failed result with the agent code.</summary>
    [Fact]
    public async Task An_error_payload_maps_to_a_failed_result_with_the_agent_code()
    {
        var stub = new StubBackupService
        {
            DeleteResponse = new DeleteBackupResponse
            {
                Error = new AgentError { Code = ErrorCode.NotFound, Message = "no such artifact" },
            },
        };

        var result = await NewClient(stub).DeleteAsync("alice", "b1", Local(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
    }

    /// <summary>A delete ok maps to success.</summary>
    [Fact]
    public async Task A_delete_ok_maps_to_success()
    {
        var stub = new StubBackupService { DeleteResponse = new DeleteBackupResponse { Ok = new DeleteBackupOk() } };

        var result = await NewClient(stub).DeleteAsync("alice", "b1", Local(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("b1", stub.LastDeleteRequest!.BackupId);
    }

    /// <summary>An unset oneof maps to the invalid response error.</summary>
    [Fact]
    public async Task An_unset_oneof_maps_to_the_invalid_response_error()
    {
        var result = await NewClient(new StubBackupService()).DeleteAsync(
            "alice",
            "b1",
            Local(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Each probe verdict maps to its own member.</summary>
    /// <remarks>
    /// Including the unspecified one, which is the arm that matters: a verdict this build cannot name
    /// must not become "private", because only "private" may save a destination.
    /// </remarks>
    [Theory]
    [InlineData(PublicReadVerdict.Private, AgentPublicReadVerdict.Private)]
    [InlineData(PublicReadVerdict.PubliclyReadable, AgentPublicReadVerdict.PubliclyReadable)]
    [InlineData(PublicReadVerdict.Unproven, AgentPublicReadVerdict.Unproven)]
    [InlineData(PublicReadVerdict.Unspecified, AgentPublicReadVerdict.Unspecified)]
    public async Task Each_probe_verdict_maps_to_its_own_member(
        PublicReadVerdict wire,
        AgentPublicReadVerdict expected)
    {
        var stub = new StubBackupService
        {
            ProbeResponse = new ProbeDestinationResponse { Ok = new ProbeDestinationOk { Verdict = wire } },
        };

        var result = await NewClient(stub).ProbeAsync(S3(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    /// <summary>An unproven probe logs its reason rather than returning it.</summary>
    [Fact]
    public async Task An_unproven_probe_logs_its_reason_rather_than_returning_it()
    {
        var logger = new RecordingLogger<AgentBackupClient>();
        var stub = new StubBackupService
        {
            ProbeResponse = new ProbeDestinationResponse
            {
                Ok = new ProbeDestinationOk
                {
                    Verdict = PublicReadVerdict.Unproven,
                    UnprovenReason = "dns lookup failed for bucket.example.net",
                },
            },
        };

        var result = await new AgentBackupClient(stub, logger).ProbeAsync(S3(), CancellationToken.None);

        Assert.Equal(AgentPublicReadVerdict.Unproven, result.Value);
        Assert.Contains(logger.Messages, message =>
        {
            return message.Contains("dns lookup failed", StringComparison.Ordinal);
        });
    }

    /// <summary>The probe refusal is surfaced as its own code.</summary>
    /// <remarks>
    /// The agent that ships today performs no probe and answers not-implemented for every
    /// destination. That has to reach the panel as itself, so that a caller can refuse to save the
    /// destination and say why, rather than reading a generic failure and retrying forever.
    /// </remarks>
    [Fact]
    public async Task The_probe_refusal_is_surfaced_as_its_own_code()
    {
        var stub = new StubBackupService
        {
            ProbeResponse = new ProbeDestinationResponse
            {
                Error = new AgentError { Code = ErrorCode.NotImplemented, Message = "no probe in this build" },
            },
        };

        var result = await NewClient(stub).ProbeAsync(S3(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotImplemented", result.Error!.Code);
    }

    /// <summary>A create stream ends with the artifact it wrote.</summary>
    [Fact]
    public async Task A_create_stream_ends_with_the_artifact_it_wrote()
    {
        var stub = new StubBackupService();
        stub.CreateResponses.Add(new CreateBackupResponse
        {
            Progress = new Progress { Percent = 40, Stage = "dumping_databases" },
        });
        stub.CreateResponses.Add(new CreateBackupResponse
        {
            Ok = new CreateBackupOk { SizeBytes = 90, Sha256 = "deadbeef", DatabaseCount = 2 },
        });

        var events = await DrainAsync(NewClient(stub).CreateAsync("alice", "b1", Local(), CancellationToken.None));

        Assert.Equal(2, events.Count);
        Assert.Equal(BackupCreateEventKind.Progress, events[0].Kind);
        Assert.Equal("dumping_databases", events[0].Stage);
        Assert.Equal(BackupCreateEventKind.Created, events[1].Kind);
        Assert.Equal("deadbeef", events[1].Sha256);
        Assert.Equal(90ul, events[1].SizeBytes);
        Assert.Equal(2ul, events[1].DatabaseCount);
    }

    /// <summary>A create stream that stops without an outcome is a truncation.</summary>
    /// <remarks>
    /// Not a completion: there may or may not be an artifact on the destination, and a panel that
    /// wrote a completed row here would offer a restore from a backup that does not exist.
    /// </remarks>
    [Fact]
    public async Task A_create_stream_that_stops_without_an_outcome_is_a_truncation()
    {
        var stub = new StubBackupService();
        stub.CreateResponses.Add(new CreateBackupResponse
        {
            Progress = new Progress { Percent = 10, Stage = "archiving_files" },
        });

        var events = await DrainAsync(NewClient(stub).CreateAsync("alice", "b1", Local(), CancellationToken.None));

        Assert.Equal(BackupCreateEventKind.Truncated, events[^1].Kind);
    }

    /// <summary>Each stream ending code becomes its own create kind.</summary>
    [Theory]
    [InlineData(ErrorCode.StreamDropped, BackupCreateEventKind.Dropped)]
    [InlineData(ErrorCode.StreamIdle, BackupCreateEventKind.Idle)]
    [InlineData(ErrorCode.SystemFailure, BackupCreateEventKind.Failed)]
    public async Task Each_stream_ending_code_becomes_its_own_create_kind(
        ErrorCode code,
        BackupCreateEventKind expected)
    {
        var stub = new StubBackupService();
        stub.CreateResponses.Add(new CreateBackupResponse { Error = new AgentError { Code = code } });

        var events = await DrainAsync(NewClient(stub).CreateAsync("alice", "b1", Local(), CancellationToken.None));

        Assert.Equal(expected, Assert.Single(events).Kind);
    }

    /// <summary>A create stream refused by an agent that has no such destination says so by its own code.</summary>
    /// <remarks>
    /// The streaming refusal is a different code path from the unary one: it arrives as the LAST item
    /// of a healthy stream rather than as a failed call, so it is the ending most likely to be read as
    /// "something went wrong" and written down as a generic failure. The kind alone cannot tell the
    /// operator apart a destination this build cannot serve from a disk that filled up, so this test
    /// asserts the CODE and not just the kind, and it sends progress first so that a refusal arriving
    /// after real work is not swallowed by it.
    /// </remarks>
    [Fact]
    public async Task A_create_stream_refused_as_not_implemented_ends_with_its_own_code()
    {
        var stub = new StubBackupService();
        stub.CreateResponses.Add(new CreateBackupResponse
        {
            Progress = new Progress { Percent = 5, Stage = "archiving_files" },
        });
        stub.CreateResponses.Add(new CreateBackupResponse
        {
            Error = new AgentError { Code = ErrorCode.NotImplemented, Message = "s3 destinations are refused" },
        });

        var events = await DrainAsync(NewClient(stub).CreateAsync("alice", "b1", S3(), CancellationToken.None));

        Assert.Equal(2, events.Count);
        Assert.Equal(BackupCreateEventKind.Progress, events[0].Kind);
        Assert.Equal(BackupCreateEventKind.Failed, events[1].Kind);
        Assert.Equal("AgentNotImplemented", events[1].ErrorCode);
    }

    /// <summary>A restore stream refused as not implemented ends with its own code.</summary>
    /// <remarks>
    /// Same ending on the other stream, and it matters more here: a restore refused because the agent
    /// cannot reach the destination has touched nothing, while a generic failure reads as a restore
    /// that may have half-run.
    /// </remarks>
    [Fact]
    public async Task A_restore_stream_refused_as_not_implemented_ends_with_its_own_code()
    {
        var stub = new StubBackupService();
        stub.RestoreResponses.Add(new RestoreBackupResponse
        {
            Error = new AgentError { Code = ErrorCode.NotImplemented, Message = "s3 destinations are refused" },
        });

        var events = await DrainAsync(NewClient(stub).RestoreAsync(
            "alice",
            "b1",
            S3(),
            "deadbeef",
            [],
            CancellationToken.None));

        var terminal = Assert.Single(events);
        Assert.Equal(BackupRestoreEventKind.Failed, terminal.Kind);
        Assert.Equal("AgentNotImplemented", terminal.Failure?.Code);
        Assert.Equal(ErrorType.Failure, terminal.Failure?.Type);
        Assert.Null(terminal.Outcome);
    }

    /// <summary>A cancelled create reports cancellation rather than truncation.</summary>
    [Fact]
    public async Task A_cancelled_create_reports_cancellation_rather_than_truncation()
    {
        using var cancellation = new CancellationTokenSource();
        var stub = new StubBackupService { OnCreateYielded = cancellation.Cancel };
        stub.CreateResponses.Add(new CreateBackupResponse { Progress = new Progress { Percent = 5 } });
        stub.CreateResponses.Add(new CreateBackupResponse { Progress = new Progress { Percent = 6 } });

        var events = await DrainAsync(NewClient(stub).CreateAsync("alice", "b1", Local(), cancellation.Token));

        Assert.Equal(BackupCreateEventKind.Cancelled, events[^1].Kind);
    }

    /// <summary>A restore reports what it did rather than that it succeeded.</summary>
    /// <remarks>
    /// The terminal KIND says the agent stated an outcome; the outcome says how much of the restore
    /// happened. A partial restore reaches the caller as an outcome whose totals disagree, and never
    /// as a kind that reads like success on its own.
    /// </remarks>
    [Fact]
    public async Task A_restore_reports_what_it_did_rather_than_that_it_succeeded()
    {
        var stub = new StubBackupService();
        stub.RestoreResponses.Add(new RestoreBackupResponse
        {
            Ok = new RestoreBackupOk { FilesRestored = true, DatabasesRestored = 1, DatabasesTotal = 3 },
        });

        var events = await DrainAsync(NewClient(stub).RestoreAsync(
            "alice",
            "b1",
            Local(),
            "deadbeef",
            ["alice_shop"],
            CancellationToken.None));

        var terminal = Assert.Single(events);
        Assert.Equal(BackupRestoreEventKind.Restored, terminal.Kind);
        Assert.True(terminal.Outcome!.FilesRestored);
        Assert.Equal(1u, terminal.Outcome.DatabasesRestored);
        Assert.Equal(3u, terminal.Outcome.DatabasesTotal);
    }

    /// <summary>A restore stream that stops without an outcome is a truncation.</summary>
    [Fact]
    public async Task A_restore_stream_that_stops_without_an_outcome_is_a_truncation()
    {
        var stub = new StubBackupService();
        stub.RestoreResponses.Add(new RestoreBackupResponse
        {
            Progress = new Progress { Percent = 20, Stage = "restoring_databases" },
        });

        var events = await DrainAsync(NewClient(stub).RestoreAsync(
            "alice",
            "b1",
            Local(),
            "deadbeef",
            [],
            CancellationToken.None));

        Assert.Equal(BackupRestoreEventKind.Truncated, events[^1].Kind);
        Assert.Null(events[^1].Outcome);
    }

    /// <summary>A failed restore carries a code and never the agent's sentence.</summary>
    /// <remarks>
    /// The agent's text on this path names the databases it rolled back and the ones it could not.
    /// That is operator-facing detail about a customer's data, so it is logged at this boundary and
    /// the event carries a machine-stable code alone.
    /// </remarks>
    [Fact]
    public async Task A_failed_restore_carries_a_code_and_never_the_agents_sentence()
    {
        var logger = new RecordingLogger<AgentBackupClient>();
        var stub = new StubBackupService();
        stub.RestoreResponses.Add(new RestoreBackupResponse
        {
            Error = new AgentError
            {
                Code = ErrorCode.SystemFailure,
                Message = "rolled back alice_shop; could not roll back alice_blog",
            },
        });

        var events = await DrainAsync(new AgentBackupClient(stub, logger).RestoreAsync(
            "alice",
            "b1",
            Local(),
            "deadbeef",
            [],
            CancellationToken.None));

        var terminal = Assert.Single(events);
        Assert.Equal(BackupRestoreEventKind.Failed, terminal.Kind);
        Assert.Equal("AgentSystemFailure", terminal.Failure?.Code);
        Assert.Equal(ErrorType.Failure, terminal.Failure?.Type);
        Assert.Contains(logger.Messages, message =>
        {
            return message.Contains("could not roll back alice_blog", StringComparison.Ordinal);
        });
    }

    /// <summary>The restore request carries the digest and the allowed databases.</summary>
    [Fact]
    public async Task The_restore_request_carries_the_digest_and_the_allowed_databases()
    {
        var stub = new StubBackupService();
        stub.RestoreResponses.Add(new RestoreBackupResponse { Ok = new RestoreBackupOk() });

        await DrainAsync(NewClient(stub).RestoreAsync(
            "alice",
            "b1",
            Local(),
            "deadbeef",
            ["alice_shop", "alice_blog"],
            CancellationToken.None));

        Assert.Equal("deadbeef", stub.LastRestoreRequest!.ExpectedSha256);
        Assert.Equal(["alice_shop", "alice_blog"], stub.LastRestoreRequest.AllowedDatabases);
    }

    /// <summary>An S3 destination reaches the wire with its credentials.</summary>
    /// <remarks>
    /// The credentials travel per call because the agent holds no destination configuration of its
    /// own, so this is the one place the panel's wrapper is unwrapped — and a client that quietly
    /// dropped them would produce an authentication failure nobody could explain.
    /// </remarks>
    [Fact]
    public async Task An_s3_destination_reaches_the_wire_with_its_credentials()
    {
        var stub = new StubBackupService { DeleteResponse = new DeleteBackupResponse { Ok = new DeleteBackupOk() } };

        await NewClient(stub).DeleteAsync("alice", "b1", S3(), CancellationToken.None);

        var destination = stub.LastDeleteRequest!.Destination;
        Assert.Equal(BackupDestinationKind.S3, destination.Kind);
        Assert.Equal("maran-backups", destination.S3Bucket);
        Assert.Equal(SecretKey, destination.S3SecretAccessKey);
    }

    /// <summary>Formatting a destination never prints the secret.</summary>
    /// <remarks>
    /// This record's compiler-generated <c>ToString</c> prints every property, which is exactly how a
    /// secret reaches a log without anyone writing a line that looks wrong. The wrapper is what makes
    /// that rendering a mask, and this test is what holds a future edit to it.
    /// </remarks>
    [Fact]
    public void Formatting_a_destination_never_prints_the_secret()
    {
        var formatted = $"{S3()}";

        Assert.DoesNotContain(SecretKey, formatted, StringComparison.Ordinal);
        Assert.Contains("maran-backups", formatted, StringComparison.Ordinal);
    }

    /// <summary>A quoted secret access key never reaches the log.</summary>
    /// <remarks>
    /// The agent's natural way of reporting a rejected credential is to quote it. The panel minted
    /// the value seconds ago, so the translator is told what it sent and looks for that exact string.
    /// </remarks>
    [Fact]
    public async Task A_quoted_secret_access_key_never_reaches_the_log()
    {
        var logger = new RecordingLogger<AgentBackupClient>();
        var stub = new StubBackupService
        {
            DeleteResponse = new DeleteBackupResponse
            {
                Error = new AgentError
                {
                    Code = ErrorCode.SystemFailure,
                    Message = $"signature mismatch using key {SecretKey}",
                },
            },
        };

        await new AgentBackupClient(stub, logger).DeleteAsync("alice", "b1", S3(), CancellationToken.None);

        Assert.NotEmpty(logger.Messages);
        Assert.DoesNotContain(logger.Messages, message =>
        {
            return message.Contains(SecretKey, StringComparison.Ordinal);
        });
    }

    /// <summary>Builds a listing holding one fully described backup.</summary>
    /// <returns>The success payload of a healthy <c>ListBackups</c>.</returns>
    private static ListBackupsOk ReadableListing()
    {
        var manifest = new BackupManifest
        {
            Version = 1,
            Account = "alice",
            BackupId = "b1",
            CreatedAtUnix = 1_700_000_000,
            HomeBytes = 2048,
            AgentVersion = "0.1.0",
        };
        manifest.Databases.Add(new ManifestDatabase { Name = "alice_shop", Bytes = 512, Sha256 = "feed" });

        var ok = new ListBackupsOk();
        ok.Backups.Add(new BackupInfo
        {
            BackupId = "b1",
            SizeBytes = 4096,
            CreatedAtUnix = 1_700_000_000,
            Sha256 = "abc123",
            Readable = new ReadableBackup
            {
                Manifest = manifest,
                ArtifactBytes = 4096,
                ArtifactSha256 = "abc123",
            },
        });

        return ok;
    }

    /// <summary>The agent's own root-only backup directory.</summary>
    /// <returns>A local destination.</returns>
    private static AgentBackupDestination Local()
    {
        return new AgentBackupDestination(
            AgentBackupDestinationKind.Local,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            new SensitiveString(string.Empty),
            new SensitiveString(string.Empty),
            false);
    }

    /// <summary>An object-store destination, which the agent that ships today refuses.</summary>
    /// <returns>An S3 destination carrying credentials.</returns>
    private static AgentBackupDestination S3()
    {
        return new AgentBackupDestination(
            AgentBackupDestinationKind.S3,
            "accounts/alice",
            "maran-backups",
            "eu-central-1",
            "https://s3.example.net",
            new SensitiveString("AKIAIOSFODNN7EXAMPLE"),
            new SensitiveString(SecretKey),
            true);
    }

    /// <summary>Reads a stream to its end under the test deadline.</summary>
    /// <typeparam name="T">The event type the stream carries.</typeparam>
    /// <param name="events">The stream under test.</param>
    /// <returns>Every event the stream produced, in order.</returns>
    private static async Task<List<T>> DrainAsync<T>(IAsyncEnumerable<T> events)
    {
        async Task<List<T>> ReadAsync()
        {
            var collected = new List<T>();
            await foreach (var item in events)
            {
                collected.Add(item);
            }

            return collected;
        }

        return await ReadAsync().WaitAsync(StreamTimeout);
    }

    /// <summary>Builds the client over a stub transport and a logger that discards.</summary>
    /// <param name="stub">The canned transport.</param>
    /// <returns>The client under test.</returns>
    private static AgentBackupClient NewClient(StubBackupService stub)
    {
        return new AgentBackupClient(stub, NullLogger<AgentBackupClient>.Instance);
    }
}
