using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Client.Services.SslService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.SslService;

/// <summary>Mapping contract of AgentSslClient (proto oneof → Result).</summary>
public sealed class AgentSslClientTests
{
    /// <summary>
    /// A site descriptor whose five fields all carry a non-default value, so the request assertion
    /// covers the whole spec — an empty one here re-renders a PHP site as static on the server.
    /// </summary>
    private static readonly SiteDescriptor Descriptor =
        new(["www.example.com"], SiteBackendKind.Php, "8.3", "127.0.0.1:3000", true);

    /// <summary>Ok payload maps to success result carrying the expiry.</summary>
    [Fact]
    public async Task Ok_payload_maps_to_success_result_carrying_the_expiry()
    {
        var stub = new StubSslService
        {
            InstallResponse = new InstallCertificateResponse
            {
                Ok = new InstallCertificateOk { ExpiresAtUnix = 1_800_000_000 },
            },
        };

        var result = await InstallAsync(stub, NullLogger<AgentSslClient>.Instance);

        Assert.True(result.IsSuccess);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), result.Value.ExpiresAt);
    }

    /// <summary>Error payload maps to failed result with agent code.</summary>
    [Fact]
    public async Task Error_payload_maps_to_failed_result_with_agent_code()
    {
        var stub = new StubSslService
        {
            InstallResponse = new InstallCertificateResponse
            {
                Error = new AgentError { Code = ErrorCode.InvalidInput, Message = "key does not match" },
            },
        };

        var result = await InstallAsync(stub, NullLogger<AgentSslClient>.Instance);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidInput", result.Error!.Code);
    }

    /// <summary>Unset oneof maps to invalid response error.</summary>
    [Fact]
    public async Task Unset_oneof_maps_to_invalid_response_error()
    {
        var result = await InstallAsync(new StubSslService(), NullLogger<AgentSslClient>.Instance);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>The agents message and tool output are logged and never returned.</summary>
    [Fact]
    public async Task The_agents_message_and_tool_output_are_logged_and_never_returned()
    {
        var logger = new RecordingLogger<AgentSslClient>();
        var stub = new StubSslService
        {
            InstallResponse = new InstallCertificateResponse
            {
                Error = new AgentError
                {
                    Code = ErrorCode.SystemFailure,
                    Message = "cannot write /var/lib/maran/certs/example.com/fullchain.pem",
                    ToolOutput = "openssl: error opening /var/lib/maran/certs/example.com/privkey.pem",
                },
            },
        };

        var result = await InstallAsync(stub, logger);

        var returned = result.Error!.ToString();
        Assert.Equal("AgentSystemFailure", result.Error.Code);
        Assert.DoesNotContain("/var/lib/maran", returned, StringComparison.Ordinal);
        var logged = Assert.Single(logger.Messages);
        Assert.Contains("/var/lib/maran/certs/example.com/fullchain.pem", logged, StringComparison.Ordinal);
        Assert.Contains("privkey.pem", logged, StringComparison.Ordinal);
    }

    /// <summary>The private key is passed to the agent and never written to the log.</summary>
    [Fact]
    public async Task The_private_key_is_passed_to_the_agent_and_never_written_to_the_log()
    {
        var logger = new RecordingLogger<AgentSslClient>();
        var stub = new StubSslService
        {
            InstallResponse = new InstallCertificateResponse
            {
                Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "reload failed" },
            },
        };

        await InstallAsync(stub, logger);

        Assert.Equal("-----BEGIN PRIVATE KEY-----secret", stub.LastRequest!.PrivateKeyPem);
        Assert.DoesNotContain(logger.Messages, message =>
        {
            return message.Contains("secret", StringComparison.Ordinal);
        });
    }

    /// <summary>Key material the agent quotes back is stripped before the text is logged.</summary>
    /// <remarks>
    /// The natural way for an agent to report an unparsable key is to quote what it could not parse,
    /// so the key reaches this client inside <c>AgentError.message</c> — a route the interface's own
    /// "never logged" promise did not cover until the translator redacted it.
    /// </remarks>
    [Fact]
    public async Task Key_material_the_agent_quotes_back_is_stripped_before_the_text_is_logged()
    {
        var logger = new RecordingLogger<AgentSslClient>();
        var stub = new StubSslService
        {
            InstallResponse = new InstallCertificateResponse
            {
                Error = new AgentError
                {
                    Code = ErrorCode.InvalidInput,
                    Message = "unparsable key: -----BEGIN PRIVATE KEY-----secret-----END PRIVATE KEY-----",
                    ToolOutput = "openssl: -----BEGIN RSA PRIVATE KEY-----alsosecret",
                },
            },
        };

        var result = await InstallAsync(stub, logger);

        Assert.Equal("AgentInvalidInput", result.Error!.Code);
        var logged = Assert.Single(logger.Messages);
        Assert.DoesNotContain("secret", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN PRIVATE KEY", logged, StringComparison.Ordinal);
        Assert.Contains("unparsable key:", logged, StringComparison.Ordinal);
    }

    /// <summary>A bare key fragment with no pem armour is stripped before the text is logged.</summary>
    /// <remarks>
    /// The realistic shape of the leak: a parser rejecting a malformed key quotes the offending
    /// bytes, not the armoured block, so there is no BEGIN marker to anchor on. The material itself
    /// is what must not reach the log, and a long unbroken run of base64 is never something an
    /// operator needs to read.
    /// </remarks>
    [Fact]
    public async Task A_bare_key_fragment_with_no_pem_armour_is_stripped_before_the_text_is_logged()
    {
        var logger = new RecordingLogger<AgentSslClient>();
        var stub = new StubSslService
        {
            InstallResponse = new InstallCertificateResponse
            {
                Error = new AgentError
                {
                    Code = ErrorCode.InvalidInput,
                    Message = "invalid base64 at line 3: MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQ",
                    ToolOutput = "openssl: rejected DQYJKoZIhvcNAQkBFhBhZG1pbkBleGFtcGxlLmNvbTCCASIwDQ==",
                },
            },
        };

        var result = await InstallAsync(stub, logger);

        Assert.Equal("AgentInvalidInput", result.Error!.Code);
        var logged = Assert.Single(logger.Messages);
        Assert.DoesNotContain("MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQ", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("DQYJKoZIhvcNAQkBFhBhZG1pbkBleGFtcGxlLmNvbTCCASIwDQ==", logged, StringComparison.Ordinal);
        Assert.Contains("invalid base64 at line 3:", logged, StringComparison.Ordinal);
        Assert.Contains("openssl: rejected", logged, StringComparison.Ordinal);
    }

    /// <summary>Install sends the account the domain the material and all five site facts.</summary>
    [Fact]
    public async Task Install_sends_the_account_the_domain_the_material_and_all_five_site_facts()
    {
        var stub = new StubSslService();

        await InstallAsync(stub, NullLogger<AgentSslClient>.Instance);

        var request = stub.LastRequest!;
        Assert.Equal("acc1", request.AccountUsername);
        Assert.Equal("example.com", request.Domain);
        Assert.Equal("-----BEGIN CERTIFICATE-----", request.CertificatePem);
        Assert.Equal("-----BEGIN PRIVATE KEY-----secret", request.PrivateKeyPem);
        Assert.Equal(["www.example.com"], request.Site.Aliases);
        Assert.Equal(SiteBackendType.Php, request.Site.BackendType);
        Assert.Equal("8.3", request.Site.PhpVersion);
        Assert.Equal("127.0.0.1:3000", request.Site.ProxyUpstream);
        Assert.True(request.Site.HasCertificate);
    }

    /// <summary>Calls the production install path with fixed arguments.</summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <param name="logger">The logger the client writes the agent's text to.</param>
    /// <returns>What the client returned.</returns>
    private static async Task<SharedKernel.Results.Result<InstalledCertificateDto>> InstallAsync(
        StubSslService stub,
        Microsoft.Extensions.Logging.ILogger<AgentSslClient> logger)
    {
        var client = new AgentSslClient(stub, logger);

        return await client.InstallCertificateAsync(
            "acc1",
            "example.com",
            "-----BEGIN CERTIFICATE-----",
            "-----BEGIN PRIVATE KEY-----secret",
            Descriptor,
            CancellationToken.None);
    }

    /// <summary>Removal ok payload maps to success.</summary>
    [Fact]
    public async Task Removal_ok_payload_maps_to_success()
    {
        var stub = new StubSslService
        {
            RemoveResponse = new RemoveCertificateResponse { Ok = new RemoveCertificateOk() },
        };

        var result = await new AgentSslClient(stub, NullLogger<AgentSslClient>.Instance)
            .RemoveCertificateAsync("acc1", "example.com", Descriptor, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>Removal error payload maps to a failed result with the agent code and no agent text.</summary>
    [Fact]
    public async Task Removal_error_payload_maps_to_a_failed_result_with_the_agent_code_and_no_agent_text()
    {
        var stub = new StubSslService
        {
            RemoveResponse = new RemoveCertificateResponse
            {
                Error = new AgentError
                {
                    Code = ErrorCode.NotFound,
                    Message = "no certificate at /var/lib/maran/certs/example.com",
                },
            },
        };

        var result = await new AgentSslClient(stub, NullLogger<AgentSslClient>.Instance)
            .RemoveCertificateAsync("acc1", "example.com", Descriptor, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);

        // The Error carries a code and never the agent's sentence, which names a host path.
        Assert.DoesNotContain("/var/lib/maran", result.Error.Code, StringComparison.Ordinal);
    }

    /// <summary>A removal response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task A_removal_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await new AgentSslClient(new StubSslService(), NullLogger<AgentSslClient>.Instance)
            .RemoveCertificateAsync("acc1", "example.com", Descriptor, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Removal sends the account the domain and the plain http descriptor.</summary>
    [Fact]
    public async Task Removal_sends_the_account_the_domain_and_the_plain_http_descriptor()
    {
        var stub = new StubSslService
        {
            RemoveResponse = new RemoveCertificateResponse { Ok = new RemoveCertificateOk() },
        };

        await new AgentSslClient(stub, NullLogger<AgentSslClient>.Instance).RemoveCertificateAsync(
            "acc1",
            "example.com",
            new SiteDescriptor(["www.example.com"], SiteBackendKind.Php, "8.3", string.Empty, false),
            CancellationToken.None);

        var request = Assert.IsType<RemoveCertificateRequest>(stub.LastRemoveRequest);
        Assert.Equal("acc1", request.AccountUsername);
        Assert.Equal("example.com", request.Domain);
        Assert.False(request.Site.HasCertificate);
        Assert.Equal(SiteBackendType.Php, request.Site.BackendType);
    }
}
