using System.Runtime.CompilerServices;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.AppsService;

/// <summary>A scripted <c>AppsService</c> transport: the wire responses a test wants the client to read.</summary>
internal sealed class StubAppsService : IAppsServiceInvoker
{
    /// <summary>What <c>ListApps</c> answers.</summary>
    public ListAppsResponse ListResponse { get; set; } = new();

    /// <summary>What <c>ListInstallableApps</c> answers.</summary>
    public ListInstallableAppsResponse InstallableResponse { get; set; } = new();

    /// <summary>The messages of the install stream, in order.</summary>
    public List<InstallAppResponse> InstallResponses { get; } = [];

    /// <summary>The messages of the uninstall stream, in order.</summary>
    public List<UninstallAppResponse> UninstallResponses { get; } = [];

    /// <summary>What <c>ControlAppService</c> answers.</summary>
    public ControlAppServiceResponse ControlResponse { get; set; } = new();

    /// <summary>The last control request received.</summary>
    public ControlAppServiceRequest? LastControlRequest { get; private set; }

    /// <summary>The last install request received.</summary>
    public InstallAppRequest? LastInstallRequest { get; private set; }

    /// <inheritdoc />
    public Task<ListAppsResponse> ListAppsAsync(ListAppsRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(ListResponse);
    }

    /// <inheritdoc />
    public Task<ListInstallableAppsResponse> ListInstallableAppsAsync(
        ListInstallableAppsRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(InstallableResponse);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<InstallAppResponse> InstallAppAsync(
        InstallAppRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LastInstallRequest = request;

        foreach (var response in InstallResponses)
        {
            await Task.Yield();
            yield return response;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<UninstallAppResponse> UninstallAppAsync(
        UninstallAppRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var response in UninstallResponses)
        {
            await Task.Yield();
            yield return response;
        }
    }

    /// <inheritdoc />
    public Task<ControlAppServiceResponse> ControlAppServiceAsync(
        ControlAppServiceRequest request,
        CancellationToken cancellationToken)
    {
        LastControlRequest = request;
        return Task.FromResult(ControlResponse);
    }
}
