using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Maran.Agent.Client.Channels;
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

    /// <summary>Creates a client whose every call goes to the server the current agent target selects.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentFilesClient(IAgentChannels channels, ILogger<AgentFilesClient> logger)
        : this(new GrpcFilesServiceInvoker(channels), logger)
    {
    }

    /// <summary>Creates a client over one fixed <paramref name="channel"/>, whatever the target.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentFilesClient(GrpcChannel channel, ILogger<AgentFilesClient> logger)
        : this(new FixedAgentChannels(channel), logger)
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
            _ => Result<ulong>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<ulong>> WriteFileStreamAsync(
        string accountUsername,
        string path,
        uint mode,
        Stream body,
        ulong maxBytes,
        CancellationToken cancellationToken)
    {
        var header = new WriteFileHeader
        {
            AccountUsername = accountUsername,
            Path = path,
            Mode = mode,
            MaxBytes = maxBytes,
            FailIfExists = false,
        };
        WriteFileResponse response;
        try
        {
            response = await _invoker.WriteFileStreamAsync(header, body, cancellationToken);
        }
        catch (RpcException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The operation pipeline does not run this call (its body cannot be replayed), so the
            // transport failure it would have mapped is mapped here: no answer from the agent is a
            // typed failure, never an anonymous 500.
            return Result<ulong>.Fail(AgentTransportFailure.ToError(exception));
        }
        catch (AgentEndpointRefusedException exception)
        {
            return Result<ulong>.Fail(exception.Error);
        }

        return response.ResultCase switch
        {
            WriteFileResponse.ResultOneofCase.Ok => Result<ulong>.Ok(response.Ok.BytesWritten),
            WriteFileResponse.ResultOneofCase.Error => Result<ulong>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(WriteFileStreamAsync))),
            _ => Result<ulong>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result> DeleteEntryAsync(
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
            DeleteEntryResponse.ResultOneofCase.Ok => Result.Ok(),
            DeleteEntryResponse.ResultOneofCase.Error => Result.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(DeleteEntryAsync))),
            _ => Result.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<DirectoryListingDto>> ListDirectoryAsync(
        string accountUsername,
        string path,
        uint offset,
        uint limit,
        bool includeHidden,
        DirectorySortOrder sort,
        bool descending,
        CancellationToken cancellationToken)
    {
        var request = new ListDirectoryRequest
        {
            AccountUsername = accountUsername,
            Path = path,
            Offset = offset,
            Limit = limit,
            IncludeHidden = includeHidden,
            Sort = FileEntryMapper.ToWire(sort),
            Descending = descending,
        };
        var response = await _invoker.ListDirectoryAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            ListDirectoryResponse.ResultOneofCase.Ok => Result<DirectoryListingDto>.Ok(
                FileEntryMapper.ToDto(response.Ok)),
            ListDirectoryResponse.ResultOneofCase.Error => Result<DirectoryListingDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ListDirectoryAsync))),
            _ => Result<DirectoryListingDto>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<FileSearchResultDto>> SearchEntriesAsync(
        string accountUsername,
        string path,
        string query,
        uint maxDepth,
        uint maxResults,
        bool includeHidden,
        CancellationToken cancellationToken)
    {
        var request = new SearchEntriesRequest
        {
            AccountUsername = accountUsername,
            Path = path,
            Query = query,
            MaxDepth = maxDepth,
            MaxResults = maxResults,
            IncludeHidden = includeHidden,
        };
        var response = await _invoker.SearchEntriesAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            SearchEntriesResponse.ResultOneofCase.Ok => FileEntryMapper.Read(response.Ok),
            SearchEntriesResponse.ResultOneofCase.Error => Result<FileSearchResultDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(SearchEntriesAsync))),
            _ => Result<FileSearchResultDto>.Fail(
                Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<OpenedFileRead>> ReadFileAsync(
        string accountUsername,
        string path,
        ulong maxBytes,
        CancellationToken cancellationToken)
    {
        var request = new ReadFileRequest
        {
            AccountUsername = accountUsername,
            Path = path,
            MaxBytes = maxBytes,
        };
        var responses = _invoker.ReadFileAsync(request, cancellationToken).GetAsyncEnumerator(cancellationToken);
        ReadFileResponse? first = null;

        try
        {
            if (await responses.MoveNextAsync())
            {
                first = responses.Current;
            }
        }
        catch (RpcException exception) when (!cancellationToken.IsCancellationRequested)
        {
            await responses.DisposeAsync();

            return Result<OpenedFileRead>.Fail(AgentTransportFailure.ToError(exception));
        }
        catch (AgentEndpointRefusedException exception)
        {
            await responses.DisposeAsync();

            return Result<OpenedFileRead>.Fail(exception.Error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A connection that could not be made is reported as a cancellation nobody asked for
            // (see AgentStreamGuard), and takes the answer a cancelled status takes.
            await responses.DisposeAsync();

            return Result<OpenedFileRead>.Fail(
                AgentTransportFailure.ToError(new RpcException(new Status(StatusCode.Cancelled, string.Empty))));
        }
        catch
        {
            await responses.DisposeAsync();
            throw;
        }

        // The first message decides whether there is a stream at all. An error here is the agent
        // refusing before the first byte (a file over max_bytes, an entry it will not open), so it is
        // an ordinary failed result and nothing was held.
        if (first is not null && first.ResultCase != ReadFileResponse.ResultOneofCase.Ok)
        {
            await responses.DisposeAsync();

            return Result<OpenedFileRead>.Fail(first.ResultCase == ReadFileResponse.ResultOneofCase.Error
                ? AgentErrorTranslator.ToError(_logger, first.Error, nameof(ReadFileAsync))
                : Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure));
        }

        return Result<OpenedFileRead>.Ok(new OpenedFileRead(responses, first, _logger));
    }

    /// <inheritdoc/>
    public async Task<Result> CreateDirectoryAsync(
        string accountUsername,
        string path,
        uint mode,
        CancellationToken cancellationToken)
    {
        var request = new CreateDirectoryRequest
        {
            AccountUsername = accountUsername,
            Path = path,
            Mode = mode,
        };
        var response = await _invoker.CreateDirectoryAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            CreateDirectoryResponse.ResultOneofCase.Ok => Result.Ok(),
            CreateDirectoryResponse.ResultOneofCase.Error => Result.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(CreateDirectoryAsync))),
            _ => Result.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The agent has no create-file rpc, so this is <c>WriteFile</c> with an empty body and
    /// <c>fail_if_exists</c> set: the write that can never replace what is already at the name.
    /// </remarks>
    public async Task<Result> CreateFileAsync(
        string accountUsername,
        string path,
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
                FailIfExists = true,
            },
            Chunk = ByteString.Empty,
        };
        var response = await _invoker.WriteFileAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            WriteFileResponse.ResultOneofCase.Ok => Result.Ok(),
            WriteFileResponse.ResultOneofCase.Error => Result.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(CreateFileAsync))),
            _ => Result.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result> MoveEntryAsync(
        string accountUsername,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var request = new MoveEntryRequest
        {
            AccountUsername = accountUsername,
            SourcePath = sourcePath,
            DestinationPath = destinationPath,
        };
        var response = await _invoker.MoveEntryAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            MoveEntryResponse.ResultOneofCase.Ok => Result.Ok(),
            MoveEntryResponse.ResultOneofCase.Error => Result.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(MoveEntryAsync))),
            _ => Result.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result> CopyEntryAsync(
        string accountUsername,
        string sourcePath,
        string destinationPath,
        ulong maxEntries,
        ulong maxBytes,
        CancellationToken cancellationToken)
    {
        var request = new CopyEntryRequest
        {
            AccountUsername = accountUsername,
            SourcePath = sourcePath,
            DestinationPath = destinationPath,
            MaxEntries = maxEntries,
            MaxBytes = maxBytes,
        };
        var response = await _invoker.CopyEntryAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            CopyEntryResponse.ResultOneofCase.Ok => Result.Ok(),
            CopyEntryResponse.ResultOneofCase.Error => Result.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(CopyEntryAsync))),
            _ => Result.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<PermissionChangeDto>> ChangePermissionsAsync(
        string accountUsername,
        string path,
        uint mode,
        bool recursive,
        uint directoryMode,
        CancellationToken cancellationToken)
    {
        var request = new ChangePermissionsRequest
        {
            AccountUsername = accountUsername,
            Path = path,
            Mode = mode,
            Recursive = recursive,
            DirectoryMode = directoryMode,
        };
        var response = await _invoker.ChangePermissionsAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            ChangePermissionsResponse.ResultOneofCase.Ok => Result<PermissionChangeDto>.Ok(new PermissionChangeDto(response.Ok.Changed, response.Ok.Skipped)),
            ChangePermissionsResponse.ResultOneofCase.Error => Result<PermissionChangeDto>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ChangePermissionsAsync))),
            _ => Result<PermissionChangeDto>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<string>> TrashEntryAsync(
        string accountUsername,
        string path,
        CancellationToken cancellationToken)
    {
        var request = new TrashEntryRequest
        {
            AccountUsername = accountUsername,
            Path = path,
        };
        var response = await _invoker.TrashEntryAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            TrashEntryResponse.ResultOneofCase.Ok => Result<string>.Ok(response.Ok.TrashId),
            TrashEntryResponse.ResultOneofCase.Error => Result<string>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(TrashEntryAsync))),
            _ => Result<string>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<TrashedEntryDto>>> ListTrashAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        var request = new ListTrashRequest
        {
            AccountUsername = accountUsername,
        };
        var response = await _invoker.ListTrashAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            ListTrashResponse.ResultOneofCase.Ok => Result<IReadOnlyList<TrashedEntryDto>>.Ok(response.Ok.Entries.Select(TrashedEntryMapper.ToDto).ToList()),
            ListTrashResponse.ResultOneofCase.Error => Result<IReadOnlyList<TrashedEntryDto>>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ListTrashAsync))),
            _ => Result<IReadOnlyList<TrashedEntryDto>>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<string>> RestoreTrashEntryAsync(
        string accountUsername,
        string trashId,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var request = new RestoreTrashEntryRequest
        {
            AccountUsername = accountUsername,
            TrashId = trashId,
            DestinationPath = destinationPath,
        };
        var response = await _invoker.RestoreTrashEntryAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            RestoreTrashEntryResponse.ResultOneofCase.Ok => Result<string>.Ok(response.Ok.RestoredPath),
            RestoreTrashEntryResponse.ResultOneofCase.Error => Result<string>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(RestoreTrashEntryAsync))),
            _ => Result<string>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<ulong>> EmptyTrashAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        var request = new EmptyTrashRequest
        {
            AccountUsername = accountUsername,
        };
        var response = await _invoker.EmptyTrashAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            EmptyTrashResponse.ResultOneofCase.Ok => Result<ulong>.Ok(response.Ok.EntriesRemoved),
            EmptyTrashResponse.ResultOneofCase.Error => Result<ulong>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(EmptyTrashAsync))),
            _ => Result<ulong>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<ulong>> PurgeExpiredTrashAsync(
        string accountUsername,
        uint olderThanDays,
        CancellationToken cancellationToken)
    {
        var request = new PurgeExpiredTrashRequest
        {
            AccountUsername = accountUsername,
            OlderThanDays = olderThanDays,
        };
        var response = await _invoker.PurgeExpiredTrashAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            PurgeExpiredTrashResponse.ResultOneofCase.Ok => Result<ulong>.Ok(response.Ok.EntriesRemoved),
            PurgeExpiredTrashResponse.ResultOneofCase.Error => Result<ulong>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(PurgeExpiredTrashAsync))),
            _ => Result<ulong>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result> ForceDeleteEntryAsync(
        string accountUsername,
        string path,
        CancellationToken cancellationToken)
    {
        var request = new ForceDeleteEntryRequest
        {
            AccountUsername = accountUsername,
            Path = path,
        };
        var response = await _invoker.ForceDeleteEntryAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            ForceDeleteEntryResponse.ResultOneofCase.Ok => Result.Ok(),
            ForceDeleteEntryResponse.ResultOneofCase.Error => Result.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ForceDeleteEntryAsync))),
            _ => Result.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<ulong>> CreateArchiveAsync(
        string accountUsername,
        IReadOnlyList<string> sourcePaths,
        string destinationPath,
        AgentArchiveFormat format,
        ulong maxEntries,
        ulong maxSourceBytes,
        CancellationToken cancellationToken)
    {
        var request = new CreateArchiveRequest
        {
            AccountUsername = accountUsername,
            DestinationPath = destinationPath,
            Format = ArchiveFormatMapper.ToWire(format),
            MaxEntries = maxEntries,
            MaxSourceBytes = maxSourceBytes,
        };
        request.SourcePaths.AddRange(sourcePaths);
        var response = await _invoker.CreateArchiveAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            CreateArchiveResponse.ResultOneofCase.Ok => Result<ulong>.Ok(response.Ok.SizeBytes),
            CreateArchiveResponse.ResultOneofCase.Error => Result<ulong>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(CreateArchiveAsync))),
            _ => Result<ulong>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<uint>> ExtractArchiveAsync(
        string accountUsername,
        string archivePath,
        string destinationPath,
        AgentArchiveFormat format,
        ulong maxEntries,
        ulong maxExpandedBytes,
        uint maxRatio,
        CancellationToken cancellationToken)
    {
        var request = new ExtractArchiveRequest
        {
            AccountUsername = accountUsername,
            ArchivePath = archivePath,
            DestinationPath = destinationPath,
            Format = ArchiveFormatMapper.ToWire(format),
            MaxEntries = maxEntries,
            MaxExpandedBytes = maxExpandedBytes,
            MaxRatio = maxRatio,
        };
        var response = await _invoker.ExtractArchiveAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            ExtractArchiveResponse.ResultOneofCase.Ok => Result<uint>.Ok(response.Ok.EntryCount),
            ExtractArchiveResponse.ResultOneofCase.Error => Result<uint>.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(ExtractArchiveAsync))),
            _ => Result<uint>.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }
}
