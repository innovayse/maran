using Google.Protobuf;
using Maran.Agent.Client.Services.TransferService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Wire = Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.TransferService;

/// <summary>Mapping contract of the unary calls of AgentTransferClient (proto oneof to a source result).</summary>
public sealed class AgentTransferClientTests
{
    /// <summary>A fragment unique enough to hunt for in anything that was printed or logged.</summary>
    private const string Sentinel = "PLANTED-PRIVATE-KEY-SENTINEL-7f3a91";

    /// <summary>The private key document the credentials in these tests carry; it holds the sentinel.</summary>
    private const string PrivateKeyText =
        "-----BEGIN OPENSSH PRIVATE KEY-----\n" + Sentinel + "AAAAbm9uZQAAAAAA\n-----END OPENSSH PRIVATE KEY-----\n";

    /// <summary>The pinned host key the credentials carry.</summary>
    private const string HostKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIPinned";

    /// <summary>A generated key pair maps to the panel's mirror, the private half behind the mask.</summary>
    [Fact]
    public async Task A_generated_key_maps_with_the_private_half_behind_the_mask()
    {
        var stub = new StubTransferService { GenerateResponse = new Wire.GenerateSourceKeyResponse { Ok = WireKey() } };

        var result = await NewClient(stub).GenerateSourceKeyAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ssh-ed25519 AAAAC3public", result.Value.PublicKey);
        Assert.Equal("SHA256:abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG", result.Value.Fingerprint);
        Assert.Equal(PrivateKeyText, result.Value.PrivateKey.Reveal());
        Assert.Equal(SourceFailureReason.Unspecified, result.Reason);
    }

    /// <summary>A key pair missing a half, or its fingerprint, is refused rather than believed.</summary>
    /// <param name="missing">Which member the wire message leaves out.</param>
    [Theory]
    [InlineData("public")]
    [InlineData("private")]
    [InlineData("fingerprint")]
    public async Task A_generated_key_missing_a_member_is_an_invalid_response(string missing)
    {
        var key = WireKey();
        if (missing == "public")
        {
            key.PublicKey = string.Empty;
        }
        else if (missing == "private")
        {
            key.PrivateKey = ByteString.Empty;
        }
        else
        {
            key.Fingerprint = string.Empty;
        }

        var result = await NewClient(new StubTransferService
        {
            GenerateResponse = new Wire.GenerateSourceKeyResponse { Ok = key },
        }).GenerateSourceKeyAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
    }

    /// <summary>A response carrying no arm is refused, and a failed one carries the agent's code only.</summary>
    [Fact]
    public async Task A_failed_generation_carries_the_code_and_a_response_with_no_arm_is_refused()
    {
        var failing = new StubTransferService
        {
            GenerateResponse = new Wire.GenerateSourceKeyResponse
            {
                Error = new Wire.AgentError { Code = Wire.ErrorCode.SystemFailure, Message = "/run/maran/scratch is full" },
            },
        };

        var failed = await NewClient(failing).GenerateSourceKeyAsync(CancellationToken.None);
        var armless = await NewClient(new StubTransferService()).GenerateSourceKeyAsync(CancellationToken.None);

        Assert.False(failed.IsSuccess);
        Assert.Equal("AgentSystemFailure", failed.Error!.Code);
        Assert.Equal(SourceFailureReason.Unspecified, failed.Reason);
        Assert.Equal("AgentInvalidResponse", armless.Error!.Code);
    }

    /// <summary>The host keys a source offered map in the agent's order, and the endpoint reaches the wire.</summary>
    [Fact]
    public async Task Scanned_host_keys_map_in_order_and_the_endpoint_reaches_the_wire()
    {
        var keys = new Wire.ScannedHostKeys();
        keys.Keys.Add(new Wire.ScannedHostKey { Algorithm = "ssh-ed25519", Key = "ssh-ed25519 AAAAone", Fingerprint = "SHA256:one" });
        keys.Keys.Add(new Wire.ScannedHostKey { Algorithm = "ssh-rsa", Key = "ssh-rsa AAAAtwo", Fingerprint = "SHA256:two" });
        var stub = new StubTransferService { ScanResponse = new Wire.ScanHostKeyResponse { Ok = keys } };

        var result = await NewClient(stub).ScanHostKeyAsync(Endpoint(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["SHA256:one", "SHA256:two"], result.Value.Select(key => { return key.Fingerprint; }).ToList());
        Assert.Equal("ssh-rsa AAAAtwo", result.Value[1].Key);
        Assert.Equal("203.0.113.9", stub.LastScanRequest!.Endpoint.Host);
        Assert.Equal(2222u, stub.LastScanRequest.Endpoint.Port);
        Assert.Equal("migrator", stub.LastScanRequest.Endpoint.User);
    }

    /// <summary>An answer with no key at all is refused: there is nothing the administrator could pin.</summary>
    /// <remarks>The inverse control is the previous test, where the same call with one key succeeds.</remarks>
    [Fact]
    public async Task A_scan_that_offers_no_key_is_an_invalid_response()
    {
        var stub = new StubTransferService
        {
            ScanResponse = new Wire.ScanHostKeyResponse { Ok = new Wire.ScannedHostKeys() },
        };

        var result = await NewClient(stub).ScanHostKeyAsync(Endpoint(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>A scanned key missing a member is refused.</summary>
    [Fact]
    public async Task A_scanned_key_missing_its_fingerprint_is_an_invalid_response()
    {
        var keys = new Wire.ScannedHostKeys();
        keys.Keys.Add(new Wire.ScannedHostKey { Algorithm = "ssh-ed25519", Key = "ssh-ed25519 AAAAone" });
        var stub = new StubTransferService { ScanResponse = new Wire.ScanHostKeyResponse { Ok = keys } };

        var result = await NewClient(stub).ScanHostKeyAsync(Endpoint(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Each failure reason of a scan reaches the caller beside the code, and none becomes a new code.</summary>
    /// <param name="wire">The reason the agent states.</param>
    /// <param name="expected">The reason the panel reports.</param>
    [Theory]
    [InlineData(Wire.SourceFailureReason.Unspecified, SourceFailureReason.Unspecified)]
    [InlineData(Wire.SourceFailureReason.HostKeyChanged, SourceFailureReason.HostKeyChanged)]
    [InlineData(Wire.SourceFailureReason.AuthRefused, SourceFailureReason.AuthRefused)]
    [InlineData(Wire.SourceFailureReason.Unreachable, SourceFailureReason.Unreachable)]
    [InlineData(Wire.SourceFailureReason.AddressRefused, SourceFailureReason.AddressRefused)]
    public async Task Each_scan_failure_reason_rides_beside_the_same_code(
        Wire.SourceFailureReason wire,
        SourceFailureReason expected)
    {
        var stub = new StubTransferService
        {
            ScanResponse = new Wire.ScanHostKeyResponse
            {
                Error = new Wire.AgentError { Code = Wire.ErrorCode.SystemFailure },
                FailureReason = wire,
            },
        };

        var result = await NewClient(stub).ScanHostKeyAsync(Endpoint(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Reason);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }

    /// <summary>A probe result maps: the credential reaches the wire, and what could not be read is absent.</summary>
    [Fact]
    public async Task A_tested_source_maps_and_the_credential_reaches_the_wire()
    {
        var stub = new StubTransferService { TestResponse = new Wire.TestSourceResponse { Ok = WireTested() } };

        var result = await NewClient(stub).TestSourceAsync(Endpoint(), Credential(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsRoot);
        Assert.Equal(1_000L, result.Value.HomeFreeBytes);
        Assert.Equal(5_000L, result.Value.HomeTotalBytes);
        Assert.Equal("Debian GNU/Linux 12", result.Value.SystemRelease);
        Assert.Equal(SourceKind.Maran, result.Value.Kind);
        Assert.Equal("1.4.2", result.Value.MaranVersion);
        Assert.Equal(SourceDatabaseServer.MariaDb, result.Value.DatabaseServer);
        Assert.Equal(SourceWebServer.Nginx, result.Value.WebServer);
        Assert.True(result.Value.MailFound);
        Assert.Equal(PrivateKeyText, stub.LastTestRequest!.Credential.PrivateKey.ToStringUtf8());
        Assert.Equal(HostKey, stub.LastTestRequest.Credential.HostKey);
    }

    /// <summary>A probe that could not run leaves its member absent, never at the wire's zero.</summary>
    [Fact]
    public async Task Probes_that_could_not_run_leave_their_members_absent()
    {
        var tested = new Wire.TestedSource { IsRoot = false, HomeFreeBytes = 7, HomeTotalBytes = 9, DiskKnown = false };
        var stub = new StubTransferService { TestResponse = new Wire.TestSourceResponse { Ok = tested } };

        var result = await NewClient(stub).TestSourceAsync(Endpoint(), Credential(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.HomeFreeBytes);
        Assert.Null(result.Value.HomeTotalBytes);
        Assert.Null(result.Value.SystemRelease);
        Assert.Null(result.Value.Kind);
        Assert.Null(result.Value.MaranVersion);
        Assert.Null(result.Value.DatabaseServer);
        Assert.Null(result.Value.WebServer);
    }

    /// <summary>A panel version is reported only for a source that is a Maran host.</summary>
    [Fact]
    public async Task A_panel_version_is_reported_only_for_a_maran_host()
    {
        var tested = WireTested();
        tested.Kind = Wire.SourceKind.GenericLinux;

        var result = await NewClient(new StubTransferService
        {
            TestResponse = new Wire.TestSourceResponse { Ok = tested },
        }).TestSourceAsync(Endpoint(), Credential(), CancellationToken.None);

        Assert.Equal(SourceKind.GenericLinux, result.Value.Kind);
        Assert.Null(result.Value.MaranVersion);
    }

    /// <summary>A disk figure too large for a signed 64-bit count is refused, and the same call with a fitting one succeeds.</summary>
    [Fact]
    public async Task A_disk_figure_that_does_not_fit_is_an_invalid_response()
    {
        var tooLarge = WireTested();
        tooLarge.HomeFreeBytes = (ulong)long.MaxValue + 1;
        var fits = WireTested();
        fits.HomeFreeBytes = (ulong)long.MaxValue;

        var refused = await NewClient(new StubTransferService
        {
            TestResponse = new Wire.TestSourceResponse { Ok = tooLarge },
        }).TestSourceAsync(Endpoint(), Credential(), CancellationToken.None);
        var accepted = await NewClient(new StubTransferService
        {
            TestResponse = new Wire.TestSourceResponse { Ok = fits },
        }).TestSourceAsync(Endpoint(), Credential(), CancellationToken.None);

        Assert.Equal("AgentInvalidResponse", refused.Error!.Code);
        Assert.Equal(long.MaxValue, accepted.Value.HomeFreeBytes);
    }

    /// <summary>A host key that changed is reported as such, with the code of the failure beside it.</summary>
    [Fact]
    public async Task A_changed_host_key_is_reported_as_the_reason_of_a_failed_test()
    {
        var stub = new StubTransferService
        {
            TestResponse = new Wire.TestSourceResponse
            {
                Error = new Wire.AgentError { Code = Wire.ErrorCode.ValidationFailed },
                FailureReason = Wire.SourceFailureReason.HostKeyChanged,
            },
        };

        var result = await NewClient(stub).TestSourceAsync(Endpoint(), Credential(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SourceFailureReason.HostKeyChanged, result.Reason);
        Assert.Equal("AgentValidationFailed", result.Error!.Code);
    }

    /// <summary>A port outside 1..65535 reaches the agent as a value the agent refuses, never as a valid one.</summary>
    /// <param name="port">The port the caller typed.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(65_536)]
    public async Task A_port_outside_the_valid_range_never_becomes_a_valid_port_on_the_wire(int port)
    {
        var stub = new StubTransferService
        {
            ScanResponse = new Wire.ScanHostKeyResponse { Ok = new Wire.ScannedHostKeys() },
        };

        await NewClient(stub).ScanHostKeyAsync(new AgentSourceEndpoint("203.0.113.9", port, "x"), CancellationToken.None);

        var wire = stub.LastScanRequest!.Endpoint.Port;
        Assert.True(wire is 0 or > 65_535, $"port {port} reached the wire as {wire}");
    }

    /// <summary>The private key never reaches the log, even when the agent quotes it back.</summary>
    /// <remarks>
    /// The agent's natural way of reporting a refused credential is to quote it. The panel sent the key
    /// seconds ago, so the translator is told what it sent. The control shows the predicate can find the
    /// sentinel in a message that carries it, and that the log was written to at all.
    /// </remarks>
    [Fact]
    public async Task A_quoted_private_key_never_reaches_the_log()
    {
        var logger = new RecordingLogger<AgentTransferClient>();
        var stub = new StubTransferService
        {
            TestResponse = new Wire.TestSourceResponse
            {
                Error = new Wire.AgentError
                {
                    Code = Wire.ErrorCode.SystemFailure,
                    Message = "ssh: could not load " + PrivateKeyText,
                },
            },
        };

        await new AgentTransferClient(stub, logger).TestSourceAsync(Endpoint(), Credential(), CancellationToken.None);

        Assert.NotEmpty(logger.Messages);
        Assert.DoesNotContain(logger.Messages, message => { return message.Contains(Sentinel, StringComparison.Ordinal); });

        var planted = new RecordingLogger<AgentTransferClient>();
        planted.Messages.Add("quoted " + Sentinel);
        Assert.Contains(planted.Messages, message => { return message.Contains(Sentinel, StringComparison.Ordinal); });
    }

    /// <summary>Formatting a credential, a generated key or the request that carries one never prints the private key.</summary>
    [Fact]
    public void Formatting_a_credential_never_prints_the_private_key()
    {
        var credential = Credential();
        var generated = new AgentGeneratedSourceKey("ssh-ed25519 AAAApub", new SensitiveString(PrivateKeyText), "SHA256:x");

        var printed = $"{credential} | {generated} | {credential with { HostKey = "other" }}";

        Assert.DoesNotContain(Sentinel, printed, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN OPENSSH", printed, StringComparison.Ordinal);
        Assert.Contains("[redacted]", printed, StringComparison.Ordinal);
        Assert.Contains(HostKey, printed, StringComparison.Ordinal);
        Assert.Contains("ssh-ed25519 AAAApub", printed, StringComparison.Ordinal);

        // Control: the secret is genuinely held, only withheld from printing.
        Assert.Contains(Sentinel, credential.PrivateKey.Reveal(), StringComparison.Ordinal);
    }

    /// <summary>The endpoint a test aims at.</summary>
    /// <returns>An endpoint on a documentation address.</returns>
    private static AgentSourceEndpoint Endpoint()
    {
        return new AgentSourceEndpoint("203.0.113.9", 2222, "migrator");
    }

    /// <summary>A credential holding the planted private key.</summary>
    /// <returns>The credential.</returns>
    private static AgentSourceCredential Credential()
    {
        return new AgentSourceCredential(new SensitiveString(PrivateKeyText), HostKey);
    }

    /// <summary>A complete wire key pair.</summary>
    /// <returns>The success payload of a healthy <c>GenerateSourceKey</c>.</returns>
    private static Wire.GeneratedSourceKey WireKey()
    {
        return new Wire.GeneratedSourceKey
        {
            PublicKey = "ssh-ed25519 AAAAC3public",
            PrivateKey = ByteString.CopyFromUtf8(PrivateKeyText),
            Fingerprint = "SHA256:abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG",
        };
    }

    /// <summary>A fully observed wire probe result.</summary>
    /// <returns>The success payload of a healthy <c>TestSource</c>.</returns>
    private static Wire.TestedSource WireTested()
    {
        return new Wire.TestedSource
        {
            IsRoot = true,
            HomeFreeBytes = 1_000,
            HomeTotalBytes = 5_000,
            DiskKnown = true,
            SystemRelease = "Debian GNU/Linux 12",
            Kind = Wire.SourceKind.Maran,
            MaranVersion = "1.4.2",
            DatabaseServer = Wire.SourceDatabaseServer.Mariadb,
            WebServer = Wire.SourceWebServer.Nginx,
            MailFound = true,
        };
    }

    /// <summary>Builds the client over a stub transport and a logger that discards.</summary>
    /// <param name="stub">The canned transport.</param>
    /// <returns>The client under test.</returns>
    private static AgentTransferClient NewClient(StubTransferService stub)
    {
        return new AgentTransferClient(stub, NullLogger<AgentTransferClient>.Instance);
    }
}
