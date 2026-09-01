using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Client.Services.SslService;
using Maran.SharedKernel.Results;
using Polly;
using Polly.Registry;

namespace Maran.Host.Resilience;

/// <summary>
/// Puts every agent TLS operation through <see cref="AgentOperationPipeline"/>, for the reason
/// <see cref="ResilientAgentAccountsClient"/> gives: without the decorator the call has no timeout
/// at all, and a stuck unix socket hangs the HTTP request that made it.
/// </summary>
public sealed class ResilientAgentSslClient : IAgentSslClient
{
    /// <summary>The client that actually talks to the agent; this type only adds the policy.</summary>
    private readonly IAgentSslClient _inner;

    /// <summary>The named operation pipeline every call below is executed through.</summary>
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Wraps the real client with the named operation pipeline.</summary>
    /// <param name="inner">The client that actually talks to the agent.</param>
    /// <param name="pipelines">The registry the named pipeline is resolved from.</param>
    public ResilientAgentSslClient(IAgentSslClient inner, ResiliencePipelineProvider<string> pipelines)
    {
        _inner = inner;
        _pipeline = pipelines.GetPipeline(AgentOperationPipeline.Name);
    }

    /// <inheritdoc/>
    public async Task<Result<InstalledCertificateDto>> InstallCertificateAsync(
        string accountUsername,
        string domain,
        string certificatePem,
        string privateKeyPem,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.InstallCertificateAsync(
                    state.AccountUsername,
                    state.Domain,
                    state.CertificatePem,
                    state.PrivateKeyPem,
                    state.Site,
                    token);
            },
            (Client: _inner,
             AccountUsername: accountUsername,
             Domain: domain,
             CertificatePem: certificatePem,
             PrivateKeyPem: privateKeyPem,
             Site: site),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> RemoveCertificateAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.RemoveCertificateAsync(
                    state.AccountUsername, state.Domain, state.Site, token);
            },
            (Client: _inner, AccountUsername: accountUsername, Domain: domain, Site: site),
            cancellationToken);
    }
}
