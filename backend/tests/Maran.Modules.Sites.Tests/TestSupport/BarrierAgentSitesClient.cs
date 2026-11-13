using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>
/// An <see cref="IAgentSitesClient"/> that holds the first call of one named operation inside the agent
/// until the test releases it, then delegates everything to a <see cref="RecordingAgentSitesClient"/>.
/// </summary>
/// <remarks>
/// It exists because a lock test that cannot lose is not a test: with the first operation parked inside
/// the agent call, a second operation on the same site is started on purpose, and the test can observe
/// whether it got past the parked one (no lock) or waited for it (lock). Calls after the held one pass
/// straight through, so the released operation and the waiting one both finish.
/// </remarks>
public sealed class BarrierAgentSitesClient : IAgentSitesClient
{
    /// <summary>The operation whose first call is held: <c>enable</c>, <c>disable</c>, <c>delete</c> or <c>change-php</c>.</summary>
    private readonly string _heldOperation;

    /// <summary>Completed when the held call has arrived inside the agent.</summary>
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completed by the test to let the held call proceed.</summary>
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>1 until the held call has been caught.</summary>
    private int _armed = 1;

    /// <summary>Creates the client.</summary>
    /// <param name="inner">The recording double every call is delegated to.</param>
    /// <param name="heldOperation">The operation whose first call is held.</param>
    public BarrierAgentSitesClient(RecordingAgentSitesClient inner, string heldOperation)
    {
        Inner = inner;
        _heldOperation = heldOperation;
    }

    /// <summary>The recording double, whose <c>Calls</c> and <c>Descriptors</c> the test asserts on.</summary>
    public RecordingAgentSitesClient Inner { get; }

    /// <summary>Completes once the held call is inside the agent.</summary>
    public Task Entered
    {
        get
        {
            return _entered.Task;
        }
    }

    /// <summary>Lets the held call proceed.</summary>
    public void Release()
    {
        _release.TrySetResult();
    }

    /// <inheritdoc/>
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
        return Inner.CreateAsync(
            accountUsername, domain, aliases, kind, phpVersion, proxyUpstream, webServer, node, maxChildren, settingOverrides, options, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result> ChangePhpVersionAsync(
        string accountUsername,
        string domain,
        string phpVersion,
        SiteDescriptor site,
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        bool removePreviousPool,
        CancellationToken cancellationToken)
    {
        var result = await Inner.ChangePhpVersionAsync(
            accountUsername, domain, phpVersion, site, maxChildren, settingOverrides, removePreviousPool, cancellationToken);
        await HoldAsync("change-php");

        return result;
    }

    /// <inheritdoc/>
    public async Task<Result> EnableAsync(string accountUsername, string domain, SiteDescriptor site, CancellationToken cancellationToken)
    {
        var result = await Inner.EnableAsync(accountUsername, domain, site, cancellationToken);
        await HoldAsync("enable");

        return result;
    }

    /// <inheritdoc/>
    public async Task<Result> DisableAsync(string accountUsername, string domain, SiteDescriptor site, CancellationToken cancellationToken)
    {
        var result = await Inner.DisableAsync(accountUsername, domain, site, cancellationToken);
        await HoldAsync("disable");

        return result;
    }

    /// <inheritdoc/>
    public async Task<Result> DeleteAsync(
        string accountUsername,
        string domain,
        string retiredPhpVersion,
        SiteWebServer webServer,
        CancellationToken cancellationToken)
    {
        var result = await Inner.DeleteAsync(accountUsername, domain, retiredPhpVersion, webServer, cancellationToken);
        await HoldAsync("delete");

        return result;
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<SiteLogEvent> TailLogAsync(
        string accountUsername,
        string domain,
        SiteLogSource logSource,
        uint historyLines,
        CancellationToken cancellationToken)
    {
        return Inner.TailLogAsync(accountUsername, domain, logSource, historyLines, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<SiteTrafficReading>> SummariseTrafficAsync(
        string accountUsername,
        string domain,
        SiteTrafficCursor? cursor,
        CancellationToken cancellationToken)
    {
        return Inner.SummariseTrafficAsync(accountUsername, domain, cursor, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<PrunedSiteLogs>> PruneLogsAsync(
        string accountUsername,
        string domain,
        uint retainRotations,
        CancellationToken cancellationToken)
    {
        return Inner.PruneLogsAsync(accountUsername, domain, retainRotations, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result> ReloadWebServerAsync(SiteWebServer webServer, CancellationToken cancellationToken)
    {
        return Inner.ReloadWebServerAsync(webServer, cancellationToken);
    }

    /// <summary>Parks the first call of the held operation until released.</summary>
    /// <param name="operation">The operation that was just called.</param>
    private async Task HoldAsync(string operation)
    {
        if (operation == _heldOperation && Interlocked.Exchange(ref _armed, 0) == 1)
        {
            _entered.TrySetResult();
            await _release.Task;
        }
    }
}
