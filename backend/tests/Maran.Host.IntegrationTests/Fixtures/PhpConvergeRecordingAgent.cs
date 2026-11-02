using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// A sites agent double that records every <c>ChangePhpVersionAsync</c> it receives and succeeds,
/// refusing every other operation.
/// </summary>
public sealed class PhpConvergeRecordingAgent : IAgentSitesClient
{
    /// <summary>One line per call: <c>username:domain:version:removePreviousPool</c>.</summary>
    public List<string> Calls { get; } = [];

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
        Calls.Add($"{accountUsername}:{domain}:{phpVersion}:{removePreviousPool}");
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
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers a PHP version change only.");
    }

    /// <inheritdoc />
    public Task<Result> EnableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers a PHP version change only.");
    }

    /// <inheritdoc />
    public Task<Result> DisableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers a PHP version change only.");
    }

    /// <inheritdoc />
    public Task<Result> DeleteAsync(
        string accountUsername,
        string domain,
        string retiredPhpVersion,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers a PHP version change only.");
    }

    /// <inheritdoc />
    public IAsyncEnumerable<SiteLogEvent> TailLogAsync(
        string accountUsername,
        string domain,
        SiteLogSource logSource,
        uint historyLines,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers a PHP version change only.");
    }

    /// <inheritdoc />
    public Task<Result> ReloadWebServerAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers a PHP version change only.");
    }
}
