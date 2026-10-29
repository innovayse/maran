using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.SharedKernel.Results;
using Polly;
using Polly.Registry;

namespace Maran.Host.Resilience;

/// <summary>
/// Puts every agent site operation through <see cref="AgentOperationPipeline"/>: a timeout, so a
/// stuck unix-socket call cannot hang the HTTP request that made it, a bounded retry on a connection
/// to the agent that could not be established, and a typed answer — <c>AgentUnavailable</c>, a 503 —
/// in place of the exception an unreachable agent used to throw (rules/csharp.md "Every outbound call
/// goes through a named resilience pipeline").
/// </summary>
/// <remarks>
/// A decorator rather than a change inside <c>AgentSitesClient</c>, for the reason
/// <see cref="ResilientAgentAccountsClient"/> gives: the pipeline lives in the Host, and the client
/// project must not depend on the Host.
///
/// The log tail is passed straight through, deliberately. Its whole purpose is to stay open while
/// nothing happens, so an operation timeout would cut it off mid-watch, and a retry would replay
/// history the operator has already read. A tail reports every way the AGENT can end it as a terminal
/// event, so it needs no policy to stop it hanging; a failure of the transport itself is not one of
/// those events and leaves the tail as the exception the transport raised, untranslated, because
/// there is no pipeline here to translate it.
/// </remarks>
public sealed class ResilientAgentSitesClient : IAgentSitesClient
{
    /// <summary>The client that actually talks to the agent; this type only adds the policy.</summary>
    private readonly IAgentSitesClient _inner;

    /// <summary>The named operation pipeline every call below is executed through.</summary>
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Wraps the real client with the named operation pipeline.</summary>
    /// <param name="inner">The client that actually talks to the agent.</param>
    /// <param name="pipelines">The registry the named pipeline is resolved from.</param>
    public ResilientAgentSitesClient(IAgentSitesClient inner, ResiliencePipelineProvider<string> pipelines)
    {
        _inner = inner;
        _pipeline = pipelines.GetPipeline(AgentOperationPipeline.Name);
    }

    /// <inheritdoc/>
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
        return ExecuteAsync(token =>
        {
            return _inner.CreateAsync(
                accountUsername,
                domain,
                aliases,
                kind,
                phpVersion,
                proxyUpstream,
                maxChildren,
                settingOverrides,
                token);
        }, cancellationToken);
    }

    /// <inheritdoc/>
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
        return ExecuteAsync(token =>
        {
            return _inner.ChangePhpVersionAsync(
                accountUsername,
                domain,
                phpVersion,
                site,
                maxChildren,
                settingOverrides,
                removePreviousPool,
                token);
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<bool>> EnableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(token =>
        {
            return _inner.EnableAsync(accountUsername, domain, site, token);
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<bool>> DisableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(token =>
        {
            return _inner.DisableAsync(accountUsername, domain, site, token);
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<bool>> DeleteAsync(
        string accountUsername,
        string domain,
        string retiredPhpVersion,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(token =>
        {
            return _inner.DeleteAsync(accountUsername, domain, retiredPhpVersion, token);
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<bool>> ReloadWebServerAsync(CancellationToken cancellationToken)
    {
        return ExecuteAsync(token =>
        {
            return _inner.ReloadWebServerAsync(token);
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<SiteLogEvent> TailLogAsync(
        string accountUsername,
        string domain,
        SiteLogSource logSource,
        uint historyLines,
        CancellationToken cancellationToken)
    {
        return _inner.TailLogAsync(accountUsername, domain, logSource, historyLines, cancellationToken);
    }

    /// <summary>Runs one call through the pipeline, carrying the caller's cancellation into it.</summary>
    /// <typeparam name="T">The value the call produces.</typeparam>
    /// <param name="call">The call to make.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>
    /// What the call returned, once the pipeline has finished with it, or the typed failure a
    /// transport failure is answered with.
    /// </returns>
    private async Task<Result<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<Result<T>>> call,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state(token);
            },
            call,
            cancellationToken);
    }
}
