using System.Net.Sockets;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.Resilience;

/// <summary>
/// An inner sites client that records what it was asked for, and can fail or stall on demand so the
/// decorator's pipeline is observable from outside.
/// </summary>
internal sealed class RecordingAgentSitesClient : IAgentSitesClient
{
    /// <summary>How long each call blocks before answering.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    /// <summary>How many calls fail with a transport error before one succeeds.</summary>
    public int FailuresBeforeSuccess { get; set; }

    /// <summary>How many times a method on this client was entered.</summary>
    public int Calls { get; private set; }

    /// <summary>The account username of the last call.</summary>
    public string? LastAccountUsername { get; private set; }

    /// <summary>The domain of the last call.</summary>
    public string? LastDomain { get; private set; }

    /// <summary>The alias list of the last create.</summary>
    public IReadOnlyList<string>? LastAliases { get; private set; }

    /// <summary>The backend kind of the last create.</summary>
    public SiteBackendKind LastKind { get; private set; }

    /// <summary>The PHP version of the last create or version change.</summary>
    public string? LastPhpVersion { get; private set; }

    /// <summary>The proxy upstream of the last create.</summary>
    public string? LastProxyUpstream { get; private set; }

    /// <summary>The site descriptor of the last re-rendering call.</summary>
    public SiteDescriptor? LastSite { get; private set; }

    /// <summary>The worker budget of the last version change.</summary>
    public uint LastMaxChildren { get; private set; }

    /// <summary>Whether the last version change was allowed to remove the pool it left behind.</summary>
    /// <remarks>
    /// Recorded because it is a `bool` in a list of positional arguments and it decides whether a
    /// php-fpm pool is DESTROYED. A resilience wrapper that dropped it, or forwarded the wrong
    /// one, would silently start removing pools an account still needs.
    /// </remarks>
    public bool LastRemovePreviousPool { get; private set; }

    /// <summary>The php.ini overrides of the last version change.</summary>
    public IReadOnlyList<PhpSettingDto>? LastSettingOverrides { get; private set; }

    /// <summary>The log source of the last tail.</summary>
    public SiteLogSource LastLogSource { get; private set; }

    /// <summary>The history limit of the last tail.</summary>
    public uint LastHistoryLines { get; private set; }

    /// <inheritdoc/>
    public async Task<Result<CreatedSiteDto>> CreateAsync(
        string accountUsername,
        string domain,
        IReadOnlyList<string> aliases,
        SiteBackendKind kind,
        string phpVersion,
        string proxyUpstream,
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastDomain = domain;
        LastAliases = aliases;
        LastKind = kind;
        LastPhpVersion = phpVersion;
        LastProxyUpstream = proxyUpstream;
        LastMaxChildren = maxChildren;

        await AttemptAsync(cancellationToken);

        return Result<CreatedSiteDto>.Ok(new CreatedSiteDto("/home/acc1/sites/example.com"));
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> ChangePhpVersionAsync(
        string accountUsername,
        string domain,
        string phpVersion,
        SiteDescriptor site,
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        bool removePreviousPool,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastDomain = domain;
        LastPhpVersion = phpVersion;
        LastSite = site;
        LastMaxChildren = maxChildren;
        LastRemovePreviousPool = removePreviousPool;
        LastSettingOverrides = settingOverrides;

        await AttemptAsync(cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> EnableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastDomain = domain;
        LastSite = site;

        await AttemptAsync(cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DisableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastDomain = domain;
        LastSite = site;

        await AttemptAsync(cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DeleteAsync(
        string accountUsername,
        string domain,
        string retiredPhpVersion,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastDomain = domain;

        await AttemptAsync(cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> ReloadWebServerAsync(CancellationToken cancellationToken)
    {
        await AttemptAsync(cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<SiteLogEvent> TailLogAsync(
        string accountUsername,
        string domain,
        SiteLogSource logSource,
        uint historyLines,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastDomain = domain;
        LastLogSource = logSource;
        LastHistoryLines = historyLines;

        await AttemptAsync(cancellationToken);

        yield return new SiteLogEvent(SiteLogEventKind.Completed, string.Empty, false, null);
    }

    /// <summary>Counts the call, stalls for <see cref="Delay"/>, and fails while failures remain.</summary>
    /// <param name="cancellationToken">The token the pipeline cancels when its timeout fires.</param>
    /// <returns>A task that completes when this attempt is allowed to succeed.</returns>
    private async Task AttemptAsync(CancellationToken cancellationToken)
    {
        Calls++;

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        if (Calls <= FailuresBeforeSuccess)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }
    }
}
