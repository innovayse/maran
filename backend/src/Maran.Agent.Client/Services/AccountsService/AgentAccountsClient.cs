using Grpc.Net.Client;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>Maps the agent's account rpcs onto <see cref="Result{T}"/>.</summary>
/// <remarks>
/// The agent answers failures inside the response's oneof rather than as a gRPC status,
/// because "this account already exists" is an answer the panel acts on. This client turns
/// that branch into a typed <see cref="Error"/> whose code the module maps to an HTTP status,
/// and logs the agent's own diagnostic text — which is operator-facing and must not reach a
/// customer (rules/security.md item 8).
/// </remarks>
public sealed class AgentAccountsClient : IAgentAccountsClient
{
    /// <summary>
    /// Pre-compiled log delegate for a failure the agent reported. Source-generated for the
    /// same reason the system client's is: an agent that is refusing fails every call.
    /// </summary>
    private static readonly Action<ILogger, string, string, string, Exception?> LogAgentError =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Warning,
            new EventId(1, nameof(AgentAccountsClient)),
            "Agent refused {Operation} with {AgentErrorCode}: {AgentErrorMessage}");

    /// <summary>The transport seam this client drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly IAccountsServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since <see cref="Error"/> carries only a code.</summary>
    private readonly ILogger<AgentAccountsClient> _logger;

    /// <summary>Creates a client over an explicit transport seam (used by tests and by the other constructor).</summary>
    /// <param name="invoker">The transport that performs the actual calls.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    internal AgentAccountsClient(IAccountsServiceInvoker invoker, ILogger<AgentAccountsClient> logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Creates a client that calls the agent over <paramref name="channel"/>.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentAccountsClient(GrpcChannel channel, ILogger<AgentAccountsClient> logger)
        : this(new GrpcAccountsServiceInvoker(new V1.AccountsService.AccountsServiceClient(channel)), logger)
    {
    }

    /// <inheritdoc/>
    public async Task<Result<CreatedAccountDto>> CreateAsync(
        string username,
        ulong quotaBytes,
        CancellationToken cancellationToken)
    {
        var request = new CreateAccountRequest { Username = username, QuotaBytes = quotaBytes };
        var response = await _invoker.CreateAccountAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            CreateAccountResponse.ResultOneofCase.Ok => Result<CreatedAccountDto>.Ok(
                new CreatedAccountDto(response.Ok.HomeDirectory, response.Ok.Uid)),
            CreateAccountResponse.ResultOneofCase.Error => Result<CreatedAccountDto>.Fail(
                ToError(response.Error, nameof(CreateAsync))),
            _ => Result<CreatedAccountDto>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse))),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SuspendAsync(string username, CancellationToken cancellationToken)
    {
        var response = await _invoker.SuspendAccountAsync(
            new SuspendAccountRequest { Username = username },
            cancellationToken);

        return response.ResultCase switch
        {
            SuspendAccountResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            SuspendAccountResponse.ResultOneofCase.Error => Result<bool>.Fail(
                ToError(response.Error, nameof(SuspendAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse))),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> UnsuspendAsync(string username, CancellationToken cancellationToken)
    {
        var response = await _invoker.UnsuspendAccountAsync(
            new UnsuspendAccountRequest { Username = username },
            cancellationToken);

        return response.ResultCase switch
        {
            UnsuspendAccountResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            UnsuspendAccountResponse.ResultOneofCase.Error => Result<bool>.Fail(
                ToError(response.Error, nameof(UnsuspendAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse))),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<ulong>> DeleteAsync(string username, CancellationToken cancellationToken)
    {
        var response = await _invoker.DeleteAccountAsync(
            new DeleteAccountRequest { Username = username },
            cancellationToken);

        return response.ResultCase switch
        {
            DeleteAccountResponse.ResultOneofCase.Ok => Result<ulong>.Ok(response.Ok.BytesFreed),
            DeleteAccountResponse.ResultOneofCase.Error => Result<ulong>.Fail(
                ToError(response.Error, nameof(DeleteAsync))),
            _ => Result<ulong>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse))),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetQuotaAsync(
        string username,
        ulong quotaBytes,
        CancellationToken cancellationToken)
    {
        var request = new SetAccountQuotaRequest { Username = username, QuotaBytes = quotaBytes };
        var response = await _invoker.SetAccountQuotaAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            SetAccountQuotaResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            SetAccountQuotaResponse.ResultOneofCase.Error => Result<bool>.Fail(
                ToError(response.Error, nameof(SetQuotaAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse))),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<AccountUsageDto>> GetUsageAsync(string username, CancellationToken cancellationToken)
    {
        var response = await _invoker.GetAccountUsageAsync(
            new GetAccountUsageRequest { Username = username },
            cancellationToken);

        return response.ResultCase switch
        {
            GetAccountUsageResponse.ResultOneofCase.Ok => Result<AccountUsageDto>.Ok(
                new AccountUsageDto(response.Ok.UsedBytes, response.Ok.QuotaBytes)),
            GetAccountUsageResponse.ResultOneofCase.Error => Result<AccountUsageDto>.Fail(
                ToError(response.Error, nameof(GetUsageAsync))),
            _ => Result<AccountUsageDto>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse))),
        };
    }

    /// <summary>
    /// Converts a wire <see cref="AgentError"/> into a typed error, logging the agent's own
    /// sentence and any tool output on the way.
    /// </summary>
    /// <param name="error">The failure payload returned by the agent.</param>
    /// <param name="operation">Which call refused, so the log line names it.</param>
    /// <returns>The error carrying only a machine-stable code.</returns>
    private Error ToError(AgentError error, string operation)
    {
        var code = ToErrorCode(error.Code);

        // The tool output — a failing useradd's stderr, for instance — is operator-facing by
        // contract. It is logged and never returned, so no path can render it to a customer.
        LogAgentError(_logger, operation, code, $"{error.Message} {error.ToolOutput}".Trim(), null);

        return Error.Of(code);
    }

    /// <summary>Maps a wire <see cref="ErrorCode"/> to its stable "Agent*" error code string.</summary>
    /// <param name="code">The failure category reported by the agent.</param>
    /// <returns>The machine-stable code the module's resources translate.</returns>
    private static string ToErrorCode(ErrorCode code)
    {
        return code switch
        {
            ErrorCode.Unspecified => nameof(ErrorMessages.AgentUnspecified),
            ErrorCode.InvalidInput => nameof(ErrorMessages.AgentInvalidInput),
            ErrorCode.AlreadyExists => nameof(ErrorMessages.AgentAlreadyExists),
            ErrorCode.NotFound => nameof(ErrorMessages.AgentNotFound),
            ErrorCode.ValidationFailed => nameof(ErrorMessages.AgentValidationFailed),
            ErrorCode.SystemFailure => nameof(ErrorMessages.AgentSystemFailure),
            _ => nameof(ErrorMessages.AgentUnspecified),
        };
    }
}
