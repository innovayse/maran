using Grpc.Net.Client;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.FtpsService;

/// <summary>Maps the agent's FTPS rpcs onto <see cref="Result{T}"/>.</summary>
/// <remarks>
/// <para>
/// Same shape as <see cref="SftpService.AgentSftpClient"/>, deliberately: the failure branch of the
/// response oneof becomes a typed <see cref="Error"/> carrying only a code, and the agent's own
/// diagnostic text — which can name the jail's absolute path, the certificate directory and the
/// daemon's configuration file — is logged rather than returned (rules/security.md item 8). A
/// response with neither branch set is refused rather than read as success.
/// </para>
/// <para>
/// Every wire error goes through <see cref="AgentErrorTranslator"/> and there is no second path
/// around it. That is where the panel's one redaction lives, so a redaction added there for one
/// client is a redaction every client got.
/// </para>
/// <para>
/// The password travels in the request and appears in no log line: it is held in a
/// <see cref="SensitiveString"/> so nothing can print it by accident, unwrapped exactly once onto
/// the wire, and handed to the translator so that the agent quoting it back is stripped before the
/// text is logged. The realistic leak in this area is not a "using password: YES" line — it is the
/// daemon or PAM echoing the credential it refused.
/// </para>
/// <para>
/// The four daemon rpcs answer with four distinct wire messages carrying the same nine fields, so
/// each is projected onto one <see cref="FtpsStatusDto"/> by its own small mapper below. They are
/// written out rather than shared through a generic helper because the wire types have no common
/// base and no interface: any sharing would be reflection or a copy, and a copy that reads is
/// better than a reflection that cannot be reviewed.
/// </para>
/// </remarks>
public sealed class AgentFtpsClient : IAgentFtpsClient
{
    /// <summary>The transport seam this client drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly IFtpsServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since <see cref="Error"/> carries only a code.</summary>
    private readonly ILogger<AgentFtpsClient> _logger;

    /// <summary>Creates a client over an explicit transport seam (used by tests and by the other constructor).</summary>
    /// <param name="invoker">The transport that performs the actual calls.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    internal AgentFtpsClient(IFtpsServiceInvoker invoker, ILogger<AgentFtpsClient> logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Creates a client that calls the agent over <paramref name="channel"/>.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentFtpsClient(GrpcChannel channel, ILogger<AgentFtpsClient> logger)
        : this(new GrpcFtpsServiceInvoker(new V1.FtpsService.FtpsServiceClient(channel)), logger)
    {
    }

    /// <inheritdoc/>
    public async Task<Result<FtpsStatusDto>> EnableAsync(
        string hostname,
        uint passivePortMin,
        uint passivePortMax,
        string passiveAddress,
        uint maxClients,
        CancellationToken cancellationToken)
    {
        var request = new EnableFtpsRequest
        {
            Hostname = hostname,
            PassivePortMin = passivePortMin,
            PassivePortMax = passivePortMax,
            PassiveAddress = passiveAddress,
            MaxClients = maxClients,
        };
        var response = await _invoker.EnableFtpsAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            EnableFtpsResponse.ResultOneofCase.Ok => Result<FtpsStatusDto>.Ok(ToStatus(response.Ok)),
            EnableFtpsResponse.ResultOneofCase.Error => Result<FtpsStatusDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(EnableAsync))),
            _ => Result<FtpsStatusDto>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<FtpsStatusDto>> DisableAsync(CancellationToken cancellationToken)
    {
        var response = await _invoker.DisableFtpsAsync(new DisableFtpsRequest(), cancellationToken);

        return response.ResultCase switch
        {
            DisableFtpsResponse.ResultOneofCase.Ok => Result<FtpsStatusDto>.Ok(ToStatus(response.Ok)),
            DisableFtpsResponse.ResultOneofCase.Error => Result<FtpsStatusDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(DisableAsync))),
            _ => Result<FtpsStatusDto>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<FtpsStatusDto>> GetStatusAsync(string hostname, CancellationToken cancellationToken)
    {
        var request = new GetFtpsStatusRequest { Hostname = hostname };
        var response = await _invoker.GetFtpsStatusAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            GetFtpsStatusResponse.ResultOneofCase.Ok => Result<FtpsStatusDto>.Ok(ToStatus(response.Ok)),
            GetFtpsStatusResponse.ResultOneofCase.Error => Result<FtpsStatusDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(GetStatusAsync))),
            _ => Result<FtpsStatusDto>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<FtpsStatusDto>> ReloadTlsAsync(CancellationToken cancellationToken)
    {
        var response = await _invoker.ReloadFtpsTlsAsync(new ReloadFtpsTlsRequest(), cancellationToken);

        return response.ResultCase switch
        {
            ReloadFtpsTlsResponse.ResultOneofCase.Ok => Result<FtpsStatusDto>.Ok(ToStatus(response.Ok)),
            ReloadFtpsTlsResponse.ResultOneofCase.Error => Result<FtpsStatusDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ReloadTlsAsync))),
            _ => Result<FtpsStatusDto>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<string>> CreateUserAsync(
        CreateFtpsUserArguments arguments,
        CancellationToken cancellationToken)
    {
        var request = new CreateFtpsUserRequest
        {
            AccountUsername = arguments.AccountUsername,
            FtpsUsername = arguments.FtpsUsername,

            // The one place the value is unwrapped, and it is unwrapped straight onto the wire.
            Password = arguments.Password.Reveal(),
        };
        var response = await _invoker.CreateFtpsUserAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            CreateFtpsUserResponse.ResultOneofCase.Ok => Result<string>.Ok(response.Ok.FtpsUsername),
            CreateFtpsUserResponse.ResultOneofCase.Error => Result<string>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(CreateUserAsync), arguments.Password)),
            _ => Result<string>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetPasswordAsync(
        string accountUsername,
        string ftpsUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        var request = new SetFtpsPasswordRequest
        {
            AccountUsername = accountUsername,
            FtpsUsername = ftpsUsername,
            Password = password.Reveal(),
        };
        var response = await _invoker.SetFtpsPasswordAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            SetFtpsPasswordResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            SetFtpsPasswordResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(SetPasswordAsync), password)),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DeleteUserAsync(
        string accountUsername,
        string ftpsUsername,
        CancellationToken cancellationToken)
    {
        var request = new DeleteFtpsUserRequest
        {
            AccountUsername = accountUsername,
            FtpsUsername = ftpsUsername,
        };
        var response = await _invoker.DeleteFtpsUserAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            DeleteFtpsUserResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            DeleteFtpsUserResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(DeleteUserAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <summary>Projects the enable rpc's observation onto the panel's one status shape.</summary>
    /// <param name="ok">The wire payload.</param>
    /// <returns>The nine facts, unchanged.</returns>
    private static FtpsStatusDto ToStatus(EnableFtpsOk ok)
    {
        return new FtpsStatusDto(
            ok.Running,
            ok.ControlPortAnswered,
            ok.CertificatePresent,
            ok.CertificateIsSelfSigned,
            ok.CertificatePath,
            ok.PassivePortMin,
            ok.PassivePortMax,
            ok.Ipv4Only,
            ok.ForcedTls);
    }

    /// <summary>Projects the disable rpc's observation onto the panel's one status shape.</summary>
    /// <param name="ok">The wire payload.</param>
    /// <returns>The nine facts, unchanged.</returns>
    private static FtpsStatusDto ToStatus(DisableFtpsOk ok)
    {
        return new FtpsStatusDto(
            ok.Running,
            ok.ControlPortAnswered,
            ok.CertificatePresent,
            ok.CertificateIsSelfSigned,
            ok.CertificatePath,
            ok.PassivePortMin,
            ok.PassivePortMax,
            ok.Ipv4Only,
            ok.ForcedTls);
    }

    /// <summary>Projects the status rpc's observation onto the panel's one status shape.</summary>
    /// <param name="ok">The wire payload.</param>
    /// <returns>The nine facts, unchanged.</returns>
    private static FtpsStatusDto ToStatus(GetFtpsStatusOk ok)
    {
        return new FtpsStatusDto(
            ok.Running,
            ok.ControlPortAnswered,
            ok.CertificatePresent,
            ok.CertificateIsSelfSigned,
            ok.CertificatePath,
            ok.PassivePortMin,
            ok.PassivePortMax,
            ok.Ipv4Only,
            ok.ForcedTls);
    }

    /// <summary>Projects the TLS-reload rpc's observation onto the panel's one status shape.</summary>
    /// <param name="ok">The wire payload.</param>
    /// <returns>The nine facts, unchanged.</returns>
    private static FtpsStatusDto ToStatus(ReloadFtpsTlsOk ok)
    {
        return new FtpsStatusDto(
            ok.Running,
            ok.ControlPortAnswered,
            ok.CertificatePresent,
            ok.CertificateIsSelfSigned,
            ok.CertificatePath,
            ok.PassivePortMin,
            ok.PassivePortMax,
            ok.Ipv4Only,
            ok.ForcedTls);
    }
}
