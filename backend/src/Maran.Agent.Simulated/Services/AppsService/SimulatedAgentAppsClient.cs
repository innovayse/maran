using System.Runtime.CompilerServices;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AppsService;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.Seeds;
using Maran.Agent.Simulated.State;

namespace Maran.Agent.Simulated.Services.AppsService;

/// <summary>
/// The host's app store, kept in memory: an install is a canned progress sequence that ends with the app
/// appearing in the ledger, and nothing is downloaded or unpacked (issue #517).
/// </summary>
/// <remarks>
/// <para>
/// A PHP app is the same fact as a PHP runtime, so installing one adds the runtime to the ledger the PHP
/// client reads and removing one takes it out; a runtime already in that ledger (put there by the baseline
/// seed or by the version screen) is listed as an app too. Idempotent like the real agent, with exactly one
/// terminal event on every stream.
/// </para>
/// <para>
/// The catalogue is the panel's: two web servers, PHP 7.4 to 8.4 and Node.js 18, 20, 22 and 24. Only one
/// web server at a time, as on a real host. Everything offered is installable, since a simulated host has
/// no package sources to disagree.
/// </para>
/// </remarks>
public sealed class SimulatedAgentAppsClient : IAgentAppsClient
{
    /// <summary>The catalogue ids in the order the agent lists them.</summary>
    private static readonly string[] Catalogue =
    [
        "nginx", "apache",
        "php-8.4", "php-8.3", "php-8.2", "php-8.1", "php-8.0", "php-7.4",
        "nodejs-24", "nodejs-22", "nodejs-20", "nodejs-18",
    ];

    /// <summary>The stages an install or removal reports.</summary>
    private static readonly string[] Stages = ["Refreshing the package index", "Applying the packages", "Starting the service"];

    /// <summary>The simulated host's state.</summary>
    private readonly SimulatedAgentLedger _ledger;

    /// <summary>Creates a client over <paramref name="ledger"/>.</summary>
    /// <param name="ledger">The simulated host's state.</param>
    public SimulatedAgentAppsClient(SimulatedAgentLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        _ledger = ledger;
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<AgentApp>>> ListAppsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var apps = Installed();
        IReadOnlyList<AgentApp> ordered = Catalogue
            .Where(id => { return apps.ContainsKey(id); })
            .Select(id => { return ToApp(apps[id]); })
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<AgentApp>>.Ok(ordered));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<AgentInstallableApp>>> ListInstallableAppsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var apps = Installed();
        IReadOnlyList<AgentInstallableApp> offered = Catalogue
            .Select(id => { return new AgentInstallableApp(id, KindOf(id), apps.ContainsKey(id)); })
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<AgentInstallableApp>>.Ok(offered));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AppOperationEvent> InstallAppAsync(
        string appId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();

        if (!Catalogue.Contains(appId, StringComparer.Ordinal))
        {
            yield return Failed(SimulatedAgentErrors.InvalidInput());
            yield break;
        }

        if (KindOf(appId) == AgentAppKind.WebServer
            && Installed().Keys.Any(id => { return KindOf(id) == AgentAppKind.WebServer && id != appId; }))
        {
            yield return Failed(Error.Of("AgentWebServerAlreadyInstalled", ErrorType.Conflict));
            yield break;
        }

        if (!Installed().ContainsKey(appId))
        {
            foreach (var (stage, index) in Stages.Select((stage, index) => { return (stage, index); }))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    yield return new AppOperationEvent(AppOperationEventKind.Cancelled, 0, string.Empty, null);
                    yield break;
                }

                yield return new AppOperationEvent(AppOperationEventKind.Progress, (uint)(10 + (index * 30)), stage, null);
            }

            Add(appId);
        }

        yield return new AppOperationEvent(AppOperationEventKind.Completed, 100, string.Empty, null);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AppOperationEvent> UninstallAppAsync(
        string appId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();

        if (!Catalogue.Contains(appId, StringComparer.Ordinal))
        {
            yield return Failed(SimulatedAgentErrors.InvalidInput());
            yield break;
        }

        if (!Installed().ContainsKey(appId))
        {
            yield return Failed(Error.Of("AgentAppNotInstalled", ErrorType.NotFound));
            yield break;
        }

        if (IsUsedBySite(appId))
        {
            yield return Failed(Error.Of("AgentAppInUse", ErrorType.Conflict));
            yield break;
        }

        foreach (var (stage, index) in Stages.Select((stage, index) => { return (stage, index); }))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield return new AppOperationEvent(AppOperationEventKind.Cancelled, 0, string.Empty, null);
                yield break;
            }

            yield return new AppOperationEvent(AppOperationEventKind.Progress, (uint)(10 + (index * 30)), stage, null);
        }

        Remove(appId);
        yield return new AppOperationEvent(AppOperationEventKind.Completed, 100, string.Empty, null);
    }

    /// <inheritdoc />
    public Task<Result<AgentAppServiceStatus>> ControlServiceAsync(
        string appId,
        AgentAppServiceAction action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var apps = Installed();
        if (!apps.TryGetValue(appId, out var app))
        {
            return Task.FromResult(Result<AgentAppServiceStatus>.Fail(
                Error.Of("AgentAppNotInstalled", ErrorType.NotFound)));
        }

        if (app.ServiceStatus == AgentAppServiceStatus.None)
        {
            return Task.FromResult(Result<AgentAppServiceStatus>.Fail(SimulatedAgentErrors.InvalidInput()));
        }

        var status = action == AgentAppServiceAction.Stop ? AgentAppServiceStatus.Inactive : AgentAppServiceStatus.Active;
        _ledger.InstalledApps.Replace(appId, current => { return current with { ServiceStatus = status }; });

        return Task.FromResult(Result<AgentAppServiceStatus>.Ok(status));
    }

    /// <summary>The kind of a catalogue id.</summary>
    /// <param name="appId">The catalogue id.</param>
    /// <returns>The kind.</returns>
    private static AgentAppKind KindOf(string appId)
    {
        return appId is "nginx" or "apache" ? AgentAppKind.WebServer : AgentAppKind.Runtime;
    }

    /// <summary>A terminal failure event.</summary>
    /// <param name="error">The typed failure.</param>
    /// <returns>The event.</returns>
    private static AppOperationEvent Failed(Error error)
    {
        return new AppOperationEvent(AppOperationEventKind.Failed, 0, string.Empty, error);
    }

    /// <summary>Restates a ledger entry as the agent client's app.</summary>
    /// <param name="app">The ledger entry.</param>
    /// <returns>The app.</returns>
    private static AgentApp ToApp(SimulatedApp app)
    {
        return new AgentApp(app.AppId, app.Kind, app.Version, app.ServiceStatus);
    }

    /// <summary>Everything the host has: the app store's apps, and every PHP runtime the PHP ledger holds.</summary>
    /// <returns>The apps by catalogue id.</returns>
    private Dictionary<string, SimulatedApp> Installed()
    {
        var apps = _ledger.InstalledApps.Snapshot().ToDictionary(app => { return app.AppId; }, StringComparer.Ordinal);
        foreach (var runtime in _ledger.PhpRuntimes.Snapshot())
        {
            var id = "php-" + runtime.Version;
            if (Catalogue.Contains(id, StringComparer.Ordinal))
            {
                apps.TryAdd(id, new SimulatedApp(id, AgentAppKind.Runtime, runtime.Version, AgentAppServiceStatus.Active));
            }
        }

        return apps;
    }

    /// <summary>Records an installed app, and for PHP the runtime the PHP client reads.</summary>
    /// <param name="appId">The catalogue id.</param>
    private void Add(string appId)
    {
        var version = appId.StartsWith("php-", StringComparison.Ordinal) ? appId["php-".Length..]
            : appId.StartsWith("nodejs-", StringComparison.Ordinal) ? appId["nodejs-".Length..] + ".0.0"
            : "1.0.0";
        var status = appId.StartsWith("nodejs-", StringComparison.Ordinal)
            ? AgentAppServiceStatus.None
            : AgentAppServiceStatus.Active;

        _ledger.InstalledApps.Add(appId, new SimulatedApp(appId, KindOf(appId), version, status));

        if (appId.StartsWith("php-", StringComparison.Ordinal))
        {
            _ledger.PhpRuntimes.Add(
                version,
                new SimulatedPhpRuntime(version, SimulatedBaselineSeed.FpmSocketDirectory, false, []));
        }
    }

    /// <summary>Forgets an app, and for PHP the runtime the PHP client reads.</summary>
    /// <param name="appId">The catalogue id.</param>
    private void Remove(string appId)
    {
        _ledger.InstalledApps.Remove(appId);
        if (appId.StartsWith("php-", StringComparison.Ordinal))
        {
            _ledger.PhpRuntimes.Remove(appId["php-".Length..]);
        }
    }

    /// <summary>Whether a simulated vhost still uses the app, which is what the real agent checks on disk.</summary>
    /// <param name="appId">The catalogue id.</param>
    /// <returns>True when a site uses it.</returns>
    private bool IsUsedBySite(string appId)
    {
        var vhosts = _ledger.Vhosts.Snapshot();
        if (appId.StartsWith("php-", StringComparison.Ordinal))
        {
            var version = appId["php-".Length..];
            return vhosts.Any(vhost => { return vhost.PhpVersion == version; });
        }

        return KindOf(appId) == AgentAppKind.WebServer && vhosts.Count > 0;
    }
}
