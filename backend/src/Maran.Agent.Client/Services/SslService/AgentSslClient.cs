using Grpc.Net.Client;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Resources;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.SslService;

/// <summary>Maps the agent's TLS rpcs onto <see cref="Result{T}"/>.</summary>
/// <remarks>
/// Same shape as the other agent clients: the failure branch of the response oneof becomes a typed
/// <see cref="Error"/> carrying only a code, and the agent's own diagnostic text — which can name
/// the certificate store's paths — is logged rather than returned (rules/security.md item 8). The
/// private key travels in the request and appears in no log line here.
/// </remarks>
public sealed class AgentSslClient : IAgentSslClient
{
    /// <summary>The transport seam this client drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly ISslServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since <see cref="Error"/> carries only a code.</summary>
    private readonly ILogger<AgentSslClient> _logger;

    /// <summary>Creates a client over an explicit transport seam (used by tests and by the other constructor).</summary>
    /// <param name="invoker">The transport that performs the actual calls.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    internal AgentSslClient(ISslServiceInvoker invoker, ILogger<AgentSslClient> logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Creates a client that calls the agent over <paramref name="channel"/>.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentSslClient(GrpcChannel channel, ILogger<AgentSslClient> logger)
        : this(new GrpcSslServiceInvoker(new V1.SslService.SslServiceClient(channel)), logger)
    {
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
        var request = new InstallCertificateRequest
        {
            AccountUsername = accountUsername,
            Domain = domain,
            CertificatePem = certificatePem,
            PrivateKeyPem = privateKeyPem,
            Site = site.ToWire(),
        };
        var response = await _invoker.InstallCertificateAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            InstallCertificateResponse.ResultOneofCase.Ok => Result<InstalledCertificateDto>.Ok(
                new InstalledCertificateDto(DateTimeOffset.FromUnixTimeSeconds(response.Ok.ExpiresAtUnix))),
            InstallCertificateResponse.ResultOneofCase.Error => Result<InstalledCertificateDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(InstallCertificateAsync))),
            _ => Result<InstalledCertificateDto>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse))),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> RemoveCertificateAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        var request = new RemoveCertificateRequest
        {
            AccountUsername = accountUsername,
            Domain = domain,
            Site = site.ToWire(),
        };
        var response = await _invoker.RemoveCertificateAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            RemoveCertificateResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            RemoveCertificateResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(RemoveCertificateAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse))),
        };
    }
}
