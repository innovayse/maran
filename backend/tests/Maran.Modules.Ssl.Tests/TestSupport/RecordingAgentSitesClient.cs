using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// An <see cref="IAgentSitesClient"/> double for the one method this module uses: the batch
/// web-server reload at the end of a renewal pass.
/// </summary>
/// <remarks>
/// Every other member throws rather than returning a bland success. A renewal that quietly called
/// DeleteAsync would pass a test whose double returned <c>Ok</c> for everything, and the failure
/// would first be visible as a customer's site disappearing.
/// </remarks>
public sealed class RecordingAgentSitesClient : IAgentSitesClient
{
    /// <summary>The refusal the reload answers with, or null to succeed.</summary>
    private readonly Error? _reloadFailure;

    /// <summary>How many times the batch reload was asked for.</summary>
    public int ReloadCallCount { get; private set; }

    /// <summary>Creates a client whose reload succeeds, or one whose reload refuses.</summary>
    /// <param name="reloadFailure">The refusal the reload answers with, or null to succeed.</param>
    public RecordingAgentSitesClient(Error? reloadFailure = null)
    {
        _reloadFailure = reloadFailure;
    }

    /// <inheritdoc />
    public Task<Result<bool>> ReloadWebServerAsync(CancellationToken cancellationToken)
    {
        ReloadCallCount++;

        return Task.FromResult(_reloadFailure is null
            ? Result<bool>.Ok(true)
            : Result<bool>.Fail(_reloadFailure));
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
        throw new NotSupportedException("The Ssl module never creates a site.");
    }

    /// <inheritdoc />
    public Task<Result<bool>> ChangePhpVersionAsync(
        string accountUsername,
        string domain,
        string phpVersion,
        SiteDescriptor site,
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        bool removePreviousPool,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The Ssl module never rebinds a site's PHP version.");
    }

    /// <inheritdoc />
    public Task<Result<bool>> EnableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The Ssl module never enables a site.");
    }

    /// <inheritdoc />
    public Task<Result<bool>> DisableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The Ssl module never disables a site.");
    }

    /// <inheritdoc />
    public Task<Result<bool>> DeleteAsync(
        string accountUsername,
        string domain,
        string retiredPhpVersion,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The Ssl module never deletes a site.");
    }

    /// <inheritdoc />
    public IAsyncEnumerable<SiteLogEvent> TailLogAsync(
        string accountUsername,
        string domain,
        SiteLogSource logSource,
        uint historyLines,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The Ssl module never tails a log.");
    }
}
