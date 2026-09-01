using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.SitesService;

/// <summary>Mapping contract of AgentSitesClient (proto oneof → Result, and stream → typed events).</summary>
public sealed class AgentSitesClientTests
{
    /// <summary>How long any stream test may wait before it is a failure rather than a hang.</summary>
    private static readonly TimeSpan StreamTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A site descriptor whose five fields all carry a non-default value, so one assertion over the
    /// request it produces covers the whole of <c>ToWire</c> rather than the two fields that happen
    /// to be interesting.
    /// </summary>
    private static readonly SiteDescriptor Descriptor =
        new(["www.example.com"], SiteBackendKind.Php, "8.3", "127.0.0.1:3000", true);

    /// <summary>Create ok payload maps to success result.</summary>
    [Fact]
    public async Task Create_ok_payload_maps_to_success_result()
    {
        var stub = new StubSitesService
        {
            CreateResponse = new CreateSiteResponse
            {
                Ok = new CreateSiteOk { DocumentRoot = "/home/acc1/sites/example.com" },
            },
        };

        var result = await CreateAsync(stub);

        Assert.True(result.IsSuccess);
        Assert.Equal("/home/acc1/sites/example.com", result.Value.DocumentRoot);
    }

    /// <summary>Create error payload maps to failed result with agent code.</summary>
    [Fact]
    public async Task Create_error_payload_maps_to_failed_result_with_agent_code()
    {
        var stub = new StubSitesService
        {
            CreateResponse = new CreateSiteResponse
            {
                Error = new AgentError { Code = ErrorCode.AlreadyExists, Message = "site exists" },
            },
        };

        var result = await CreateAsync(stub);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentAlreadyExists", result.Error!.Code);
    }

    /// <summary>Create unset oneof maps to invalid response error.</summary>
    [Fact]
    public async Task Create_unset_oneof_maps_to_invalid_response_error()
    {
        var result = await CreateAsync(new StubSitesService());

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Change php version ok payload maps to success result.</summary>
    [Fact]
    public async Task Change_php_version_ok_payload_maps_to_success_result()
    {
        var stub = new StubSitesService
        {
            UpdateResponse = new UpdateSitePhpVersionResponse { Ok = new UpdateSitePhpVersionOk() },
        };

        var result = await ChangePhpVersionAsync(stub);

        Assert.True(result.IsSuccess);
    }

    /// <summary>Change php version error payload maps to failed result with agent code.</summary>
    [Fact]
    public async Task Change_php_version_error_payload_maps_to_failed_result_with_agent_code()
    {
        var stub = new StubSitesService
        {
            UpdateResponse = new UpdateSitePhpVersionResponse
            {
                Error = new AgentError { Code = ErrorCode.ValidationFailed, Message = "not installed" },
            },
        };

        var result = await ChangePhpVersionAsync(stub);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentValidationFailed", result.Error!.Code);
    }

    /// <summary>Change php version unset oneof maps to invalid response error.</summary>
    [Fact]
    public async Task Change_php_version_unset_oneof_maps_to_invalid_response_error()
    {
        var result = await ChangePhpVersionAsync(new StubSitesService());

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Change php version sends the site facts and the plan values it was given.</summary>
    [Fact]
    public async Task Change_php_version_sends_the_site_facts_and_the_plan_values_it_was_given()
    {
        var stub = new StubSitesService
        {
            UpdateResponse = new UpdateSitePhpVersionResponse { Ok = new UpdateSitePhpVersionOk() },
        };

        await ChangePhpVersionAsync(stub);

        var request = stub.LastUpdateRequest!;
        Assert.Equal(SiteBackendType.Php, request.Site.BackendType);
        Assert.Equal(["www.example.com"], request.Site.Aliases);
        Assert.True(request.Site.HasCertificate);
        Assert.Equal(12u, request.MaxChildren);
        Assert.Equal("memory_limit", Assert.Single(request.Overrides).Name);
    }

    /// <summary>Enable ok payload maps to success result.</summary>
    [Fact]
    public async Task Enable_ok_payload_maps_to_success_result()
    {
        var stub = new StubSitesService { EnableResponse = new EnableSiteResponse { Ok = new EnableSiteOk() } };

        var result = await NewClient(stub).EnableAsync("acc1", "example.com", Descriptor, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>Enable error payload maps to failed result with agent code.</summary>
    [Fact]
    public async Task Enable_error_payload_maps_to_failed_result_with_agent_code()
    {
        var stub = new StubSitesService
        {
            EnableResponse = new EnableSiteResponse
            {
                Error = new AgentError { Code = ErrorCode.NotFound, Message = "no such site" },
            },
        };

        var result = await NewClient(stub).EnableAsync("acc1", "example.com", Descriptor, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
    }

    /// <summary>Enable unset oneof maps to invalid response error.</summary>
    [Fact]
    public async Task Enable_unset_oneof_maps_to_invalid_response_error()
    {
        var client = NewClient(new StubSitesService());

        var result = await client.EnableAsync("acc1", "example.com", Descriptor, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Disable ok payload maps to success result.</summary>
    [Fact]
    public async Task Disable_ok_payload_maps_to_success_result()
    {
        var stub = new StubSitesService { DisableResponse = new DisableSiteResponse { Ok = new DisableSiteOk() } };

        var result = await NewClient(stub).DisableAsync("acc1", "example.com", Descriptor, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>Disable error payload maps to failed result with agent code.</summary>
    [Fact]
    public async Task Disable_error_payload_maps_to_failed_result_with_agent_code()
    {
        var stub = new StubSitesService
        {
            DisableResponse = new DisableSiteResponse
            {
                Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "reload failed" },
            },
        };

        var result = await NewClient(stub).DisableAsync("acc1", "example.com", Descriptor, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }

    /// <summary>Disable unset oneof maps to invalid response error.</summary>
    [Fact]
    public async Task Disable_unset_oneof_maps_to_invalid_response_error()
    {
        var client = NewClient(new StubSitesService());

        var result = await client.DisableAsync("acc1", "example.com", Descriptor, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>Delete ok payload maps to success result.</summary>
    [Fact]
    public async Task Delete_ok_payload_maps_to_success_result()
    {
        var stub = new StubSitesService { DeleteResponse = new DeleteSiteResponse { Ok = new DeleteSiteOk() } };

        var result = await NewClient(stub).DeleteAsync("acc1", "example.com", "", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>Delete error payload maps to failed result with agent code.</summary>
    [Fact]
    public async Task Delete_error_payload_maps_to_failed_result_with_agent_code()
    {
        var stub = new StubSitesService
        {
            DeleteResponse = new DeleteSiteResponse
            {
                Error = new AgentError { Code = ErrorCode.NotFound, Message = "gone" },
            },
        };

        var result = await NewClient(stub).DeleteAsync("acc1", "example.com", "", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
    }

    /// <summary>Delete unset oneof maps to invalid response error.</summary>
    [Fact]
    public async Task Delete_unset_oneof_maps_to_invalid_response_error()
    {
        var client = NewClient(new StubSitesService());

        var result = await client.DeleteAsync("acc1", "example.com", "", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }

    /// <summary>The agents message and tool output are logged and never returned.</summary>
    [Fact]
    public async Task The_agents_message_and_tool_output_are_logged_and_never_returned()
    {
        var logger = new RecordingLogger<AgentSitesClient>();
        var stub = new StubSitesService
        {
            CreateResponse = new CreateSiteResponse
            {
                Error = new AgentError
                {
                    Code = ErrorCode.ValidationFailed,
                    Message = "failed to write /etc/nginx/sites-available/example.com.conf",
                    ToolOutput = "nginx: [emerg] invalid parameter in /etc/nginx/conf.d/maran.conf:12",
                },
            },
        };
        var client = new AgentSitesClient(stub, logger);

        var result = await client.CreateAsync(
            "acc1",
            "example.com",
            [],
            SiteBackendKind.Static,
            string.Empty,
            string.Empty,
            0,
            [],
            CancellationToken.None);

        var returned = result.Error!.ToString();
        Assert.Equal("AgentValidationFailed", result.Error.Code);
        Assert.DoesNotContain("/etc/nginx", returned, StringComparison.Ordinal);
        Assert.DoesNotContain("nginx: [emerg]", returned, StringComparison.Ordinal);
        var logged = Assert.Single(logger.Messages);
        Assert.Contains("/etc/nginx/sites-available/example.com.conf", logged, StringComparison.Ordinal);
        Assert.Contains("nginx: [emerg]", logged, StringComparison.Ordinal);
    }

    /// <summary>A tail that the agent closes normally ends with a completed event.</summary>
    [Fact]
    public async Task A_tail_that_the_agent_closes_normally_ends_with_a_completed_event()
    {
        var stub = new StubSitesService();
        stub.TailResponses.Add(new TailSiteLogResponse
        {
            Ok = new TailSiteLogLine { Line = "GET /", Historical = true },
        });

        var events = await CollectAsync(stub);

        Assert.Equal(2, events.Count);
        Assert.Equal(SiteLogEventKind.Line, events[0].Kind);
        Assert.Equal("GET /", events[0].Line);
        Assert.True(events[0].Historical);
        Assert.Equal(SiteLogEventKind.Completed, events[1].Kind);
    }

    /// <summary>A tail the agent dropped ends with a dropped event and not a completed one.</summary>
    [Fact]
    public async Task A_tail_the_agent_dropped_ends_with_a_dropped_event_and_not_a_completed_one()
    {
        var stub = new StubSitesService();
        stub.TailResponses.Add(new TailSiteLogResponse
        {
            Error = new AgentError { Code = ErrorCode.StreamDropped, Message = "client stopped reading" },
        });

        var events = await CollectAsync(stub);

        Assert.Equal(SiteLogEventKind.Dropped, Assert.Single(events).Kind);
    }

    /// <summary>A tail the agent closed for idleness ends with an idle event.</summary>
    [Fact]
    public async Task A_tail_the_agent_closed_for_idleness_ends_with_an_idle_event()
    {
        var stub = new StubSitesService();
        stub.TailResponses.Add(new TailSiteLogResponse
        {
            Error = new AgentError { Code = ErrorCode.StreamIdle, Message = "nothing logged" },
        });

        var events = await CollectAsync(stub);

        Assert.Equal(SiteLogEventKind.Idle, Assert.Single(events).Kind);
    }

    /// <summary>A tail that failed ends with a failed event carrying the typed code.</summary>
    [Fact]
    public async Task A_tail_that_failed_ends_with_a_failed_event_carrying_the_typed_code()
    {
        var stub = new StubSitesService();
        stub.TailResponses.Add(new TailSiteLogResponse
        {
            Error = new AgentError { Code = ErrorCode.NotFound, Message = "/var/log/nginx/example.log missing" },
        });

        var events = await CollectAsync(stub);

        var last = Assert.Single(events);
        Assert.Equal(SiteLogEventKind.Failed, last.Kind);
        Assert.Equal("AgentNotFound", last.ErrorCode);
    }

    /// <summary>A tail message carrying no branch ends with a failed event.</summary>
    [Fact]
    public async Task A_tail_message_carrying_no_branch_ends_with_a_failed_event()
    {
        var stub = new StubSitesService();
        stub.TailResponses.Add(new TailSiteLogResponse());

        var events = await CollectAsync(stub);

        var last = Assert.Single(events);
        Assert.Equal(SiteLogEventKind.Failed, last.Kind);
        Assert.Equal("AgentInvalidResponse", last.ErrorCode);
    }

    /// <summary>Create sends every field it was given.</summary>
    [Fact]
    public async Task Create_sends_every_field_it_was_given()
    {
        var stub = new StubSitesService();

        await CreateAsync(stub);

        var request = stub.LastCreateRequest!;
        Assert.Equal("acc1", request.AccountUsername);
        Assert.Equal("example.com", request.Domain);
        Assert.Equal(["www.example.com"], request.Aliases);
        Assert.Equal(SiteBackendType.Php, request.BackendType);
        Assert.Equal("8.3", request.PhpVersion);
        Assert.Equal("127.0.0.1:3000", request.ProxyUpstream);
    }

    /// <summary>Each backend kind maps to its own wire backend type.</summary>
    /// <param name="kind">The panel-side backend the caller asked for.</param>
    /// <param name="expected">The wire value the agent must receive.</param>
    [Theory]
    [InlineData(SiteBackendKind.Static, SiteBackendType.Static)]
    [InlineData(SiteBackendKind.Php, SiteBackendType.Php)]
    [InlineData(SiteBackendKind.ReverseProxy, SiteBackendType.ReverseProxy)]
    public async Task Each_backend_kind_maps_to_its_own_wire_backend_type(
        SiteBackendKind kind,
        SiteBackendType expected)
    {
        var stub = new StubSitesService();

        await NewClient(stub).CreateAsync(
            "acc1",
            "example.com",
            [],
            kind,
            "8.3",
            "127.0.0.1:3000",
            10,
            [],
            CancellationToken.None);

        Assert.Equal(expected, stub.LastCreateRequest!.BackendType);
    }

    /// <summary>Enable sends the account the domain and all five site facts.</summary>
    [Fact]
    public async Task Enable_sends_the_account_the_domain_and_all_five_site_facts()
    {
        var stub = new StubSitesService();

        await NewClient(stub).EnableAsync("acc1", "example.com", Descriptor, CancellationToken.None);

        var request = stub.LastEnableRequest!;
        Assert.Equal("acc1", request.AccountUsername);
        Assert.Equal("example.com", request.Domain);
        Assert.Equal(["www.example.com"], request.Site.Aliases);
        Assert.Equal(SiteBackendType.Php, request.Site.BackendType);
        Assert.Equal("8.3", request.Site.PhpVersion);
        Assert.Equal("127.0.0.1:3000", request.Site.ProxyUpstream);
        Assert.True(request.Site.HasCertificate);
    }

    /// <summary>Each backend kind survives the site spec of a re-rendering call.</summary>
    /// <param name="kind">The panel-side backend the site is on.</param>
    /// <param name="expected">The wire value the re-rendered vhost must be built from.</param>
    [Theory]
    [InlineData(SiteBackendKind.Static, SiteBackendType.Static)]
    [InlineData(SiteBackendKind.Php, SiteBackendType.Php)]
    [InlineData(SiteBackendKind.ReverseProxy, SiteBackendType.ReverseProxy)]
    public async Task Each_backend_kind_survives_the_site_spec_of_a_re_rendering_call(
        SiteBackendKind kind,
        SiteBackendType expected)
    {
        var stub = new StubSitesService();
        var descriptor = Descriptor with { Backend = kind };

        await NewClient(stub).EnableAsync("acc1", "example.com", descriptor, CancellationToken.None);

        Assert.Equal(expected, stub.LastEnableRequest!.Site.BackendType);
    }

    /// <summary>Disable sends the account the domain and the site facts.</summary>
    [Fact]
    public async Task Disable_sends_the_account_the_domain_and_the_site_facts()
    {
        var stub = new StubSitesService();

        await NewClient(stub).DisableAsync("acc1", "example.com", Descriptor, CancellationToken.None);

        var request = stub.LastDisableRequest!;
        Assert.Equal("acc1", request.AccountUsername);
        Assert.Equal("example.com", request.Domain);
        Assert.Equal(["www.example.com"], request.Site.Aliases);
        Assert.Equal(SiteBackendType.Php, request.Site.BackendType);
        Assert.Equal("8.3", request.Site.PhpVersion);
        Assert.Equal("127.0.0.1:3000", request.Site.ProxyUpstream);
        Assert.True(request.Site.HasCertificate);
    }

    /// <summary>Change php version sends the account the domain and the version.</summary>
    [Fact]
    public async Task Change_php_version_sends_the_account_the_domain_and_the_version()
    {
        var stub = new StubSitesService();

        await ChangePhpVersionAsync(stub);

        var request = stub.LastUpdateRequest!;
        Assert.Equal("acc1", request.AccountUsername);
        Assert.Equal("example.com", request.Domain);
        Assert.Equal("8.4", request.PhpVersion);
        Assert.Equal("8.3", request.Site.PhpVersion);
        Assert.Equal("127.0.0.1:3000", request.Site.ProxyUpstream);
        Assert.Equal("256M", Assert.Single(request.Overrides).Value);
    }

    /// <summary>Delete sends the account and the domain.</summary>
    [Fact]
    public async Task Delete_sends_the_account_and_the_domain()
    {
        var stub = new StubSitesService();

        await NewClient(stub).DeleteAsync("acc1", "example.com", "", CancellationToken.None);

        Assert.Equal("acc1", stub.LastDeleteRequest!.AccountUsername);
        Assert.Equal("example.com", stub.LastDeleteRequest.Domain);
    }

    /// <summary>Tail sends the account the domain and the history limit.</summary>
    [Fact]
    public async Task Tail_sends_the_account_the_domain_and_the_history_limit()
    {
        var stub = new StubSitesService();

        await CollectAsync(stub);

        var request = stub.LastTailRequest!;
        Assert.Equal("acc1", request.AccountUsername);
        Assert.Equal("example.com", request.Domain);
        Assert.Equal(10u, request.HistoryLines);
    }

    /// <summary>Each log source maps to its own wire log kind.</summary>
    /// <param name="source">The log the operator asked for.</param>
    /// <param name="expected">The wire value that must be requested.</param>
    [Theory]
    [InlineData(SiteLogSource.Access, SiteLogKind.Access)]
    [InlineData(SiteLogSource.Error, SiteLogKind.Error)]
    public async Task Each_log_source_maps_to_its_own_wire_log_kind(SiteLogSource source, SiteLogKind expected)
    {
        var stub = new StubSitesService();
        var client = NewClient(stub);

        async Task DrainAsync()
        {
            await foreach (var unused in client.TailLogAsync("acc1", "example.com", source, 10, default))
            {
                // The endings are asserted elsewhere; this test is about the request.
            }
        }

        await DrainAsync().WaitAsync(StreamTimeout);

        Assert.Equal(expected, stub.LastTailRequest!.Kind);
    }

    /// <summary>A cancelled tail ends with a cancelled event and stops pulling.</summary>
    [Fact]
    public async Task A_cancelled_tail_ends_with_a_cancelled_event_and_stops_pulling()
    {
        var stub = new StubSitesService();
        for (var index = 0; index < 50; index++)
        {
            stub.TailResponses.Add(new TailSiteLogResponse { Ok = new TailSiteLogLine { Line = $"line {index}" } });
        }

        using var cancellation = new CancellationTokenSource();
        var client = NewClient(stub);

        async Task<List<SiteLogEvent>> DrainAsync()
        {
            var events = new List<SiteLogEvent>();
            var stream = client.TailLogAsync("acc1", "example.com", SiteLogSource.Access, 10, cancellation.Token);
            await foreach (var item in stream)
            {
                events.Add(item);
                if (events.Count == 3)
                {
                    await cancellation.CancelAsync();
                }
            }

            return events;
        }

        var collected = await DrainAsync().WaitAsync(StreamTimeout);

        Assert.Equal(SiteLogEventKind.Cancelled, collected[^1].Kind);
        Assert.DoesNotContain(collected, item =>
        {
            return item.Kind == SiteLogEventKind.Completed;
        });
        Assert.True(stub.TailYieldedCount < 10, $"kept pulling after cancellation: {stub.TailYieldedCount} messages");
    }

    /// <summary>Builds a client over the stub with a logger that discards its output.</summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <returns>The client under test.</returns>
    private static AgentSitesClient NewClient(StubSitesService stub)
    {
        return new AgentSitesClient(stub, NullLogger<AgentSitesClient>.Instance);
    }

    /// <summary>Calls the production create path with fixed arguments.</summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <returns>What the client returned.</returns>
    private static async Task<SharedKernel.Results.Result<CreatedSiteDto>> CreateAsync(StubSitesService stub)
    {
        return await NewClient(stub).CreateAsync(
            "acc1",
            "example.com",
            ["www.example.com"],
            SiteBackendKind.Php,
            "8.3",
            "127.0.0.1:3000",
            10,
            [],
            CancellationToken.None);
    }

    /// <summary>Calls the production php-version path with fixed arguments.</summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <returns>What the client returned.</returns>
    private static async Task<SharedKernel.Results.Result<bool>> ChangePhpVersionAsync(StubSitesService stub)
    {
        return await NewClient(stub).ChangePhpVersionAsync(
            "acc1",
            "example.com",
            "8.4",
            Descriptor,
            12,
            [new PhpSettingDto("memory_limit", "256M")],
            true,
            CancellationToken.None);
    }

    /// <summary>
    /// Drains the tail stream under a hard deadline, so a stream that never ends fails the test
    /// instead of hanging the run — a hang gets blamed on CI and retried, a failure gets fixed.
    /// </summary>
    /// <param name="stub">The transport stub to drive.</param>
    /// <returns>Every event the client produced, in order.</returns>
    private static async Task<List<SiteLogEvent>> CollectAsync(StubSitesService stub)
    {
        var client = NewClient(stub);

        async Task<List<SiteLogEvent>> DrainAsync()
        {
            var events = new List<SiteLogEvent>();
            var stream = client.TailLogAsync("acc1", "example.com", SiteLogSource.Access, 10, CancellationToken.None);
            await foreach (var item in stream)
            {
                events.Add(item);
            }

            return events;
        }

        return await DrainAsync().WaitAsync(StreamTimeout);
    }

    /// <summary>A reload ok payload maps to success.</summary>
    [Fact]
    public async Task A_reload_ok_payload_maps_to_success()
    {
        var stub = new StubSitesService
        {
            ReloadResponse = new ReloadWebServerResponse { Ok = new ReloadWebServerOk() },
        };

        var result = await new AgentSitesClient(stub, NullLogger<AgentSitesClient>.Instance)
            .ReloadWebServerAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, stub.ReloadCallCount);
    }

    /// <summary>A reload the agent refuses maps to a failed result carrying only a code.</summary>
    [Fact]
    public async Task A_reload_the_agent_refuses_maps_to_a_failed_result_carrying_only_a_code()
    {
        var stub = new StubSitesService
        {
            ReloadResponse = new ReloadWebServerResponse
            {
                Error = new AgentError
                {
                    Code = ErrorCode.ValidationFailed,
                    Message = "nginx: configuration file /etc/nginx/nginx.conf test failed",
                    ToolOutput = "nginx: [emerg] cannot load certificate /var/lib/maran/certs/a.pem",
                },
            },
        };

        var result = await new AgentSitesClient(stub, NullLogger<AgentSitesClient>.Instance)
            .ReloadWebServerAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentValidationFailed", result.Error!.Code);

        // The failing nginx output names host paths and never leaves the client.
        Assert.DoesNotContain("/etc/nginx", result.Error.Code, StringComparison.Ordinal);
        Assert.DoesNotContain("/var/lib/maran", result.Error.Code, StringComparison.Ordinal);
    }

    /// <summary>A reload response with neither branch set is refused rather than read as success.</summary>
    [Fact]
    public async Task A_reload_response_with_neither_branch_set_is_refused_rather_than_read_as_success()
    {
        var result = await new AgentSitesClient(new StubSitesService(), NullLogger<AgentSitesClient>.Instance)
            .ReloadWebServerAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidResponse", result.Error!.Code);
    }
}
