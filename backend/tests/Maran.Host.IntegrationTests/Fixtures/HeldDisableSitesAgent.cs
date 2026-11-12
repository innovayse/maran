using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// A sites agent double that holds a disable inside the agent call until the test releases it, and records
/// every vhost render, so a test can put a plan-change re-render on a site in the middle of its disable.
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists because a race test that cannot LOSE is not a test.</b> The window the site lock closes is
/// the one between the agent suspending the vhost and the site row being saved as disabled: a re-render
/// that reads the row in that window still sees an enabled site and writes a live vhost over the
/// suspension stub. Holding the disable inside the agent call makes that window as long as the test wants,
/// so the re-render is certain to meet it; without the hold the two would be serialised by chance.
/// </para>
/// <para>
/// Renders are recorded in the order they arrive, with the domain and whether a disable was still held at
/// that moment, which is the fact the test asserts on.
/// </para>
/// </remarks>
public sealed class HeldDisableSitesAgent : IAgentSitesClient
{
    /// <summary>Completed when a disable has reached the agent call.</summary>
    private readonly TaskCompletionSource _disableEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completed by the test to let the held disable return.</summary>
    private readonly TaskCompletionSource _disableReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Every render: the domain and whether a disable was held inside the agent at that moment.</summary>
    private readonly List<(string Domain, bool DisableHeld)> _renders = [];

    /// <summary>Whether a disable is inside the agent call right now.</summary>
    private int _disableHeld;

    /// <summary>Completes once a disable has reached the agent call.</summary>
    public Task DisableEntered
    {
        get { return _disableEntered.Task; }
    }

    /// <summary>The renders so far, in arrival order.</summary>
    public IReadOnlyList<(string Domain, bool DisableHeld)> Renders
    {
        get
        {
            lock (_renders)
            {
                return [.. _renders];
            }
        }
    }

    /// <summary>Lets the held disable return.</summary>
    public void ReleaseDisable()
    {
        _disableReleased.TrySetResult();
    }

    /// <inheritdoc />
    public async Task<Result> DisableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _disableHeld, 1);
        _disableEntered.TrySetResult();
        await _disableReleased.Task.WaitAsync(cancellationToken);
        Interlocked.Exchange(ref _disableHeld, 0);

        return Result.Ok();
    }

    /// <inheritdoc />
    public Task<Result> ChangePhpVersionAsync(
        string accountUsername,
        string domain,
        string phpVersion,
        SiteDescriptor site,
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        bool removePreviousPool,
        CancellationToken cancellationToken)
    {
        lock (_renders)
        {
            _renders.Add((domain, Volatile.Read(ref _disableHeld) == 1));
        }

        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result<CreatedSiteDto>> CreateAsync(
        string accountUsername,
        string domain,
        IReadOnlyList<string> aliases,
        SiteBackendKind kind,
        string phpVersion,
        string proxyUpstream,
        SiteWebServer webServer,
        SiteNodeApp node,
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        SiteOptionsDescriptor options,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers a disable and a render only.");
    }

    /// <inheritdoc />
    public Task<Result> EnableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers a disable and a render only.");
    }

    /// <inheritdoc />
    public Task<Result> DeleteAsync(
        string accountUsername,
        string domain,
        string retiredPhpVersion,
        SiteWebServer webServer,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers a disable and a render only.");
    }

    /// <inheritdoc />
    public IAsyncEnumerable<SiteLogEvent> TailLogAsync(
        string accountUsername,
        string domain,
        SiteLogSource logSource,
        uint historyLines,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers a disable and a render only.");
    }

    /// <inheritdoc />
    public Task<Result<SiteTrafficReading>> SummariseTrafficAsync(
        string accountUsername,
        string domain,
        SiteTrafficCursor? cursor,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers a disable and a render only.");
    }

    /// <inheritdoc />
    public Task<Result<PrunedSiteLogs>> PruneLogsAsync(
        string accountUsername,
        string domain,
        uint retainRotations,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers a disable and a render only.");
    }

    /// <inheritdoc />
    public Task<Result> ReloadWebServerAsync(SiteWebServer webServer, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers a disable and a render only.");
    }
}
