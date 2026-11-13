using Maran.Agent.Client.Services.AppsService;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.AppsService;

/// <summary>Mapping contract of AgentAppsClient: proto oneof to Result, and the two streams to typed events.</summary>
public sealed class AgentAppsClientTests
{
    /// <summary>Creates a client over a stub.</summary>
    /// <param name="stub">The scripted transport.</param>
    /// <returns>The client.</returns>
    private static AgentAppsClient NewClient(StubAppsService stub)
    {
        return new AgentAppsClient(stub, NullLogger<AgentAppsClient>.Instance);
    }

    /// <summary>Drains the install stream.</summary>
    /// <param name="client">The client.</param>
    /// <returns>Every event.</returns>
    private static async Task<List<AppOperationEvent>> InstallAsync(AgentAppsClient client)
    {
        var events = new List<AppOperationEvent>();
        await foreach (var item in client.InstallAppAsync("nginx", CancellationToken.None))
        {
            events.Add(item);
        }

        return events;
    }

    /// <summary>The installed list maps id, kind, version and service status, in the agent's order.</summary>
    [Fact]
    public async Task The_installed_list_maps_each_app_in_order()
    {
        var ok = new ListAppsOk();
        ok.Apps.Add(new AppInfo { AppId = "nginx", Kind = AppKind.WebServer, Version = "1.24.0", ServiceStatus = Maran.Agent.V1.AppServiceStatus.Active });
        ok.Apps.Add(new AppInfo { AppId = "nodejs-22", Kind = AppKind.Runtime, Version = "22.1", ServiceStatus = Maran.Agent.V1.AppServiceStatus.None });
        var stub = new StubAppsService { ListResponse = new ListAppsResponse { Ok = ok } };

        var result = await NewClient(stub).ListAppsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [
                new AgentApp("nginx", AgentAppKind.WebServer, "1.24.0", AgentAppServiceStatus.Active),
                new AgentApp("nodejs-22", AgentAppKind.Runtime, "22.1", AgentAppServiceStatus.None),
            ],
            result.Value);
    }

    /// <summary>An unspecified kind or status is "not known", never a real value.</summary>
    [Fact]
    public async Task An_unspecified_kind_and_status_read_as_unknown()
    {
        var ok = new ListAppsOk();
        ok.Apps.Add(new AppInfo { AppId = "apache", Version = "2.4" });
        var stub = new StubAppsService { ListResponse = new ListAppsResponse { Ok = ok } };

        var result = await NewClient(stub).ListAppsAsync(CancellationToken.None);

        var app = Assert.Single(result.Value);
        Assert.Equal(AgentAppKind.Unknown, app.Kind);
        Assert.Equal(AgentAppServiceStatus.Unknown, app.ServiceStatus);
    }

    /// <summary>An agent error, and a message with no branch, map to typed failures.</summary>
    [Fact]
    public async Task An_error_and_an_unset_oneof_map_to_failures()
    {
        var failing = new StubAppsService
        {
            ListResponse = new ListAppsResponse { Error = new AgentError { Code = ErrorCode.SystemFailure, Message = "x" } },
        };

        Assert.Equal("AgentSystemFailure", (await NewClient(failing).ListAppsAsync(CancellationToken.None)).Error!.Code);
        Assert.Equal(
            "AgentInvalidResponse",
            (await NewClient(new StubAppsService()).ListAppsAsync(CancellationToken.None)).Error!.Code);
    }

    /// <summary>The installable list maps the installed flag.</summary>
    [Fact]
    public async Task The_installable_list_maps_the_installed_flag()
    {
        var ok = new ListInstallableAppsOk();
        ok.Apps.Add(new InstallableApp { AppId = "php-8.4", Kind = AppKind.Runtime, Installed = false });
        ok.Apps.Add(new InstallableApp { AppId = "nginx", Kind = AppKind.WebServer, Installed = true });
        var stub = new StubAppsService { InstallableResponse = new ListInstallableAppsResponse { Ok = ok } };

        var result = await NewClient(stub).ListInstallableAppsAsync(CancellationToken.None);

        Assert.Equal(
            [
                new AgentInstallableApp("php-8.4", AgentAppKind.Runtime, false),
                new AgentInstallableApp("nginx", AgentAppKind.WebServer, true),
            ],
            result.Value.Apps);
        Assert.Null(result.Value.NodejsUnavailable);
    }

    /// <summary>Each wire reason Node.js is missing reaches the panel's own enum; the unset value is no reason (issue #532).</summary>
    /// <param name="wire">The wire reason.</param>
    /// <param name="expected">The panel's reason, or null.</param>
    [Theory]
    [InlineData(NodejsUnavailableReason.NoModuleStreams, AgentNodejsUnavailableReason.NoModuleStreams)]
    [InlineData(NodejsUnavailableReason.ListingFailed, AgentNodejsUnavailableReason.ListingFailed)]
    [InlineData(NodejsUnavailableReason.Unspecified, null)]
    public async Task The_installable_list_maps_why_nodejs_is_missing(
        NodejsUnavailableReason wire,
        AgentNodejsUnavailableReason? expected)
    {
        var ok = new ListInstallableAppsOk { NodejsUnavailableReason = wire };
        ok.Apps.Add(new InstallableApp { AppId = "nginx", Kind = AppKind.WebServer, Installed = false });
        var stub = new StubAppsService { InstallableResponse = new ListInstallableAppsResponse { Ok = ok } };

        var result = await NewClient(stub).ListInstallableAppsAsync(CancellationToken.None);

        Assert.Equal(expected, result.Value.NodejsUnavailable);
    }

    /// <summary>A service action sends the app and the action and answers the resulting status.</summary>
    [Fact]
    public async Task A_service_action_sends_the_action_and_answers_the_status()
    {
        var stub = new StubAppsService
        {
            ControlResponse = new ControlAppServiceResponse
            {
                Ok = new ControlAppServiceOk { ServiceStatus = Maran.Agent.V1.AppServiceStatus.Inactive },
            },
        };

        var result = await NewClient(stub).ControlServiceAsync("apache", AgentAppServiceAction.Stop, CancellationToken.None);

        Assert.Equal(AgentAppServiceStatus.Inactive, result.Value);
        Assert.Equal("apache", stub.LastControlRequest!.AppId);
        Assert.Equal(Maran.Agent.V1.AppServiceAction.Stop, stub.LastControlRequest.Action);
    }

    /// <summary>An install stream maps progress and ends in exactly one completion.</summary>
    [Fact]
    public async Task An_install_stream_maps_progress_then_completion()
    {
        var stub = new StubAppsService();
        stub.InstallResponses.Add(new InstallAppResponse { Progress = new Progress { Percent = 30, Stage = "installing" } });
        stub.InstallResponses.Add(new InstallAppResponse { Ok = new InstallAppOk { AppId = "nginx" } });

        var events = await InstallAsync(NewClient(stub));

        Assert.Equal(2, events.Count);
        Assert.Equal(new AppOperationEvent(AppOperationEventKind.Progress, 30, "installing", null), events[0]);
        Assert.Equal(AppOperationEventKind.Completed, events[1].Kind);
        Assert.Equal("nginx", stub.LastInstallRequest!.AppId);
    }

    /// <summary>A stream that ends with no terminal message is a truncation, never a completion.</summary>
    [Fact]
    public async Task A_stream_without_a_terminal_message_is_a_truncation()
    {
        var stub = new StubAppsService();
        stub.InstallResponses.Add(new InstallAppResponse { Progress = new Progress { Percent = 30, Stage = "installing" } });

        var events = await InstallAsync(NewClient(stub));

        Assert.Equal(AppOperationEventKind.Truncated, events[^1].Kind);
        Assert.DoesNotContain(events, item => { return item.Kind == AppOperationEventKind.Completed; });
    }

    /// <summary>The three app-store refusals reach the stream as typed failures; the stream codes become their own endings.</summary>
    /// <param name="code">The wire code.</param>
    /// <param name="expectedCode">The panel's code.</param>
    /// <param name="expectedType">The panel's kind.</param>
    [Theory]
    [InlineData(ErrorCode.AppNotInstalled, "AgentAppNotInstalled", ErrorType.NotFound)]
    [InlineData(ErrorCode.AppInUse, "AgentAppInUse", ErrorType.Conflict)]
    [InlineData(ErrorCode.WebServerAlreadyInstalled, "AgentWebServerAlreadyInstalled", ErrorType.Conflict)]
    [InlineData(ErrorCode.NodeMajorAlreadyInstalled, "AgentNodeMajorAlreadyInstalled", ErrorType.Conflict)]
    [InlineData(ErrorCode.NodeUnmanagedInstall, "AgentNodeUnmanagedInstall", ErrorType.Conflict)]
    [InlineData(ErrorCode.NodeStreamEnabledByOperator, "AgentNodeStreamEnabledByOperator", ErrorType.Conflict)]
    [InlineData(ErrorCode.NodeInterpreterUnreadable, "AgentNodeInterpreterUnreadable", ErrorType.Conflict)]
    [InlineData(ErrorCode.NodeOperationInProgress, "AgentNodeOperationInProgress", ErrorType.Conflict)]
    public async Task An_app_store_refusal_is_a_typed_failure(ErrorCode code, string expectedCode, ErrorType expectedType)
    {
        var stub = new StubAppsService();
        stub.UninstallResponses.Add(new UninstallAppResponse { Error = new AgentError { Code = code, Message = "x" } });
        var client = NewClient(stub);

        var events = new List<AppOperationEvent>();
        await foreach (var item in client.UninstallAppAsync("nginx", CancellationToken.None))
        {
            events.Add(item);
        }

        var ending = Assert.Single(events);
        Assert.Equal(AppOperationEventKind.Failed, ending.Kind);
        Assert.Equal(expectedCode, ending.Error!.Code);
        Assert.Equal(expectedType, ending.Error.Type);
    }

    /// <summary>The agent's stream endings are not failures of the operation.</summary>
    [Fact]
    public async Task The_stream_endings_are_their_own_kinds()
    {
        var dropped = new StubAppsService();
        dropped.InstallResponses.Add(new InstallAppResponse { Error = new AgentError { Code = ErrorCode.StreamDropped } });
        var idle = new StubAppsService();
        idle.InstallResponses.Add(new InstallAppResponse { Error = new AgentError { Code = ErrorCode.StreamIdle } });

        Assert.Equal(AppOperationEventKind.Dropped, Assert.Single(await InstallAsync(NewClient(dropped))).Kind);
        Assert.Equal(AppOperationEventKind.Idle, Assert.Single(await InstallAsync(NewClient(idle))).Kind);
    }
}
