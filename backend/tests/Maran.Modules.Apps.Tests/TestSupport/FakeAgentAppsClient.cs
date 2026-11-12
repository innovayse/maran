using System.Runtime.CompilerServices;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AppsService;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Apps.Tests.TestSupport;

/// <summary>
/// An <see cref="IAgentAppsClient"/> double with scripted answers, recording the calls it received so a
/// test can assert that a refused request never reached the agent.
/// </summary>
public sealed class FakeAgentAppsClient : IAgentAppsClient
{
    /// <summary>What <c>ListApps</c> answers.</summary>
    public Result<IReadOnlyList<AgentApp>> Installed { get; set; } = Result<IReadOnlyList<AgentApp>>.Ok([]);

    /// <summary>What <c>ListInstallableApps</c> answers.</summary>
    public Result<AgentInstallableApps> Installable { get; set; } =
        Result<AgentInstallableApps>.Ok(new AgentInstallableApps([], null));

    /// <summary>The events an install or uninstall stream yields, in order.</summary>
    public List<AppOperationEvent> Stream { get; set; } = [new(AppOperationEventKind.Completed, 100, string.Empty, null)];

    /// <summary>What a service action answers.</summary>
    public Result<AgentAppServiceStatus> Control { get; set; } = Result<AgentAppServiceStatus>.Ok(AgentAppServiceStatus.Active);

    /// <summary>The ids an install was asked for.</summary>
    public List<string> InstallCalls { get; } = [];

    /// <summary>The ids a removal was asked for.</summary>
    public List<string> UninstallCalls { get; } = [];

    /// <summary>The service actions asked for, as the id and the action.</summary>
    public List<(string AppId, AgentAppServiceAction Action)> ControlCalls { get; } = [];

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<AgentApp>>> ListAppsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Installed);
    }

    /// <inheritdoc />
    public Task<Result<AgentInstallableApps>> ListInstallableAppsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Installable);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<AppOperationEvent> InstallAppAsync(string appId, CancellationToken cancellationToken)
    {
        InstallCalls.Add(appId);
        return Events(cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<AppOperationEvent> UninstallAppAsync(string appId, CancellationToken cancellationToken)
    {
        UninstallCalls.Add(appId);
        return Events(cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<AgentAppServiceStatus>> ControlServiceAsync(
        string appId,
        AgentAppServiceAction action,
        CancellationToken cancellationToken)
    {
        ControlCalls.Add((appId, action));
        return Task.FromResult(Control);
    }

    /// <summary>Yields the scripted stream.</summary>
    /// <returns>The scripted events.</returns>
    private async IAsyncEnumerable<AppOperationEvent> Events([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var scripted in Stream)
        {
            await Task.Yield();
            yield return scripted;
        }
    }
}
