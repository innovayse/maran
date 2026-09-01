using System.Text;
using Google.Protobuf;
using Grpc.Net.Client;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.FilesService;

/// <summary>Maps the agent's customer-file rpcs onto <see cref="Result{T}"/>.</summary>
/// <remarks>
/// Same shape as the other agent clients: the failure branch of the response oneof becomes a typed
/// <see cref="Error"/> carrying only a code, and the agent's own diagnostic text — which names
/// absolute paths inside a customer's home — is logged rather than returned (rules/security.md item
/// 8). The content written is never logged: this client's only caller writes an ACME challenge
/// token, and a token is a secret for as long as the order is open.
/// </remarks>
public sealed class AgentFilesClient : IAgentFilesClient
{
    /// <summary>The transport seam this client drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly IFilesServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since <see cref="Error"/> carries only a code.</summary>
    private readonly ILogger<AgentFilesClient> _logger;

    /// <summary>Creates a client over an explicit transport seam (used by tests and by the other constructor).</summary>
    /// <param name="invoker">The transport that performs the actual calls.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    internal AgentFilesClient(IFilesServiceInvoker invoker, ILogger<AgentFilesClient> logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Creates a client that calls the agent over <paramref name="channel"/>.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentFilesClient(GrpcChannel channel, ILogger<AgentFilesClient> logger)
        : this(new GrpcFilesServiceInvoker(new V1.FilesService.FilesServiceClient(channel)), logger)
    {
    }

    /// <inheritdoc/>
    public async Task<Result<ulong>> WriteFileAsync(
        string accountUsername,
        string path,
        string content,
        uint mode,
        CancellationToken cancellationToken)
    {
        var request = new WriteFileRequest
        {
            Header = new WriteFileHeader
            {
                AccountUsername = accountUsername,
                Path = path,
                Mode = mode,
            },

            // UTF-8, and the encoding is named rather than left to a default: the file is served
            // verbatim over HTTP to a certificate authority, which compares it byte for byte against
            // what it expects. Any other encoding is a validation failure with no useful diagnostic.
            //
            // No byte-order mark is involved either way — Encoding.GetBytes never emits a preamble,
            // only a StreamWriter does — so there is deliberately no "without a BOM" flag here to
            // suggest otherwise.
            Chunk = ByteString.CopyFrom(content, Encoding.UTF8),
        };
        var response = await _invoker.WriteFileAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            WriteFileResponse.ResultOneofCase.Ok => Result<ulong>.Ok(response.Ok.BytesWritten),
            WriteFileResponse.ResultOneofCase.Error => Result<ulong>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(WriteFileAsync))),
            _ => Result<ulong>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse))),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DeleteEntryAsync(
        string accountUsername,
        string path,
        bool recursive,
        CancellationToken cancellationToken)
    {
        var request = new DeleteEntryRequest
        {
            AccountUsername = accountUsername,
            Path = path,
            Recursive = recursive,
        };
        var response = await _invoker.DeleteEntryAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            DeleteEntryResponse.ResultOneofCase.Ok => Result<bool>.Ok(true),
            DeleteEntryResponse.ResultOneofCase.Error => Result<bool>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(DeleteEntryAsync))),
            _ => Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse))),
        };
    }
}
