using Grpc.Net.Client;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.DbService;

/// <summary>Maps the agent's database rpcs onto <see cref="Result{T}"/>.</summary>
/// <remarks>
/// Same shape as the other agent clients: the failure branch of the response oneof becomes a typed
/// <see cref="Error"/> carrying only a code, and the agent's own diagnostic text — which can name
/// the server's sockets and paths — is logged rather than returned (rules/security.md item 8).
///
/// The password travels in the request and appears in no log line here, by two mechanisms rather
/// than one: it is held in a <see cref="SensitiveString"/> so that nothing can print it by accident,
/// and it is handed to the error translator so that the agent quoting it back — the realistic leak,
/// since a refused credential is usually reported with the credential in it — is stripped before the
/// text is logged.
/// </remarks>
public sealed class AgentDbClient : IAgentDbClient
{
    /// <summary>The transport seam this client drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly IDbServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since <see cref="Error"/> carries only a code.</summary>
    private readonly ILogger<AgentDbClient> _logger;

    /// <summary>Creates a client over an explicit transport seam (used by tests and by the other constructor).</summary>
    /// <param name="invoker">The transport that performs the actual calls.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    internal AgentDbClient(IDbServiceInvoker invoker, ILogger<AgentDbClient> logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Creates a client that calls the agent over <paramref name="channel"/>.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentDbClient(GrpcChannel channel, ILogger<AgentDbClient> logger)
        : this(new GrpcDbServiceInvoker(new V1.DbService.DbServiceClient(channel)), logger)
    {
    }

    /// <inheritdoc/>
    public async Task<Result<CreatedDatabaseDto>> CreateAsync(
        string accountUsername,
        string databaseName,
        string dbUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        var request = new CreateDatabaseRequest
        {
            AccountUsername = accountUsername,
            DatabaseName = databaseName,
            DbUsername = dbUsername,

            // The one place the value is unwrapped, and it is unwrapped straight onto the wire.
            Password = password.Reveal(),
        };
        var response = await _invoker.CreateDatabaseAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            CreateDatabaseResponse.ResultOneofCase.Ok => Result<CreatedDatabaseDto>.Ok(
                new CreatedDatabaseDto(response.Ok.DatabaseName, response.Ok.DbUsername)),
            CreateDatabaseResponse.ResultOneofCase.Error => Result<CreatedDatabaseDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(CreateAsync), password)),
            _ => Result<CreatedDatabaseDto>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DropAsync(
        string accountUsername,
        string databaseName,
        string dbUsername,
        CancellationToken cancellationToken)
    {
        var request = new DropDatabaseRequest
        {
            AccountUsername = accountUsername,
            DatabaseName = databaseName,
            DbUsername = dbUsername,
        };
        var response = await _invoker.DropDatabaseAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            DropDatabaseResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            DropDatabaseResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(DropAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetPasswordAsync(
        string accountUsername,
        string dbUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        var request = new SetDatabasePasswordRequest
        {
            AccountUsername = accountUsername,
            DbUsername = dbUsername,

            // The one place the value is unwrapped, and it is unwrapped straight onto the wire —
            // the same shape CreateAsync uses, for the same reason.
            Password = password.Reveal(),
        };
        var response = await _invoker.SetDatabasePasswordAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            SetDatabasePasswordResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            SetDatabasePasswordResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(SetPasswordAsync), password)),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<DatabaseSummaryDto>>> ListAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        var request = new ListDatabasesRequest { AccountUsername = accountUsername };
        var response = await _invoker.ListDatabasesAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            ListDatabasesResponse.ResultOneofCase.Ok => Result<IReadOnlyList<DatabaseSummaryDto>>.Ok(
                ToSummaries(response.Ok)),
            ListDatabasesResponse.ResultOneofCase.Error => Result<IReadOnlyList<DatabaseSummaryDto>>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ListAsync))),
            _ => Result<IReadOnlyList<DatabaseSummaryDto>>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<ulong>> GetSizeAsync(
        string accountUsername,
        string databaseName,
        CancellationToken cancellationToken)
    {
        var request = new GetDatabaseSizeRequest
        {
            AccountUsername = accountUsername,
            DatabaseName = databaseName,
        };
        var response = await _invoker.GetDatabaseSizeAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            GetDatabaseSizeResponse.ResultOneofCase.Ok => Result<ulong>.Ok(response.Ok.SizeBytes),
            GetDatabaseSizeResponse.ResultOneofCase.Error => Result<ulong>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(GetSizeAsync))),
            _ => Result<ulong>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <summary>Projects the wire listing onto the panel's DTOs.</summary>
    /// <param name="ok">The success payload of <c>ListDatabases</c>.</param>
    /// <returns>
    /// The rows in the order the agent sent them, each keeping the two fields the agent deliberately
    /// leaves unset as nulls rather than as proto3 defaults. An absent user must not become an empty
    /// name and an absent size must not become zero: zero is the claim "this database is empty",
    /// which the listing never measured, and a panel could not tell it from "not known".
    /// </returns>
    private static List<DatabaseSummaryDto> ToSummaries(ListDatabasesOk ok)
    {
        var summaries = new List<DatabaseSummaryDto>(ok.Databases.Count);

        foreach (var database in ok.Databases)
        {
            summaries.Add(new DatabaseSummaryDto(
                database.DatabaseName,
                database.HasDbUsername ? database.DbUsername : null,
                database.HasSizeBytes ? database.SizeBytes : null));
        }

        return summaries;
    }
}
