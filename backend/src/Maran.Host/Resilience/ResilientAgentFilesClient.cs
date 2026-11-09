using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.FilesService;
using Maran.SharedKernel.Results;
using Polly;
using Polly.Registry;

namespace Maran.Host.Resilience;

/// <summary>
/// Puts every agent customer-file operation through <see cref="AgentOperationPipeline"/>, for the
/// reason <see cref="ResilientAgentAccountsClient"/> gives: without the decorator the call has no
/// timeout at all, and a stuck unix socket hangs the request that made it.
/// </summary>
public sealed class ResilientAgentFilesClient : IAgentFilesClient
{
    /// <summary>The client that actually talks to the agent; this type only adds the policy.</summary>
    private readonly IAgentFilesClient _inner;

    /// <summary>The named operation pipeline every call below is executed through.</summary>
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Wraps the real client with the named operation pipeline.</summary>
    /// <param name="inner">The client that actually talks to the agent.</param>
    /// <param name="pipelines">The registry the named pipeline is resolved from.</param>
    public ResilientAgentFilesClient(IAgentFilesClient inner, ResiliencePipelineProvider<string> pipelines)
    {
        _inner = inner;
        _pipeline = pipelines.GetPipeline(AgentOperationPipeline.Name);
    }

    /// <inheritdoc/>
    public async Task<Result<ulong>> WriteFileAsync(
        string accountUsername,
        string path,
        string content,
        uint mode,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.WriteFileAsync(
                    state.AccountUsername, state.Path, state.Content, state.Mode, token);
            },
            (Client: _inner, AccountUsername: accountUsername, Path: path, Content: content, Mode: mode),
            cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Deliberately NOT run through the pipeline, for two reasons. The body is a stream read once, so
    /// a retry would replay a call whose body is already consumed and write a truncated file; and an
    /// upload has no duration the operation timeout could judge, while the pipeline's token is
    /// released when the call returns. The call is bound to the caller's own token, and the inner
    /// client answers a transport that gave up as a typed failure itself.
    /// </remarks>
    public Task<Result<ulong>> WriteFileStreamAsync(
        string accountUsername,
        string path,
        uint mode,
        Stream body,
        ulong maxBytes,
        CancellationToken cancellationToken)
    {
        return _inner.WriteFileStreamAsync(accountUsername, path, mode, body, maxBytes, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result> DeleteEntryAsync(
        string accountUsername,
        string path,
        bool recursive,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.DeleteEntryAsync(
                    state.AccountUsername, state.Path, state.Recursive, token);
            },
            (Client: _inner, AccountUsername: accountUsername, Path: path, Recursive: recursive),
            cancellationToken);
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
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.ListDirectoryAsync(
                    state.AccountUsername,
                    state.Path,
                    state.Offset,
                    state.Limit,
                    state.IncludeHidden,
                    state.Sort,
                    state.Descending,
                    token);
            },
            (
                Client: _inner,
                AccountUsername: accountUsername,
                Path: path,
                Offset: offset,
                Limit: limit,
                IncludeHidden: includeHidden,
                Sort: sort,
                Descending: descending),
            cancellationToken);
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
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.SearchEntriesAsync(
                    state.AccountUsername,
                    state.Path,
                    state.Query,
                    state.MaxDepth,
                    state.MaxResults,
                    state.IncludeHidden,
                    token);
            },
            (
                Client: _inner,
                AccountUsername: accountUsername,
                Path: path,
                Query: query,
                MaxDepth: maxDepth,
                MaxResults: maxResults,
                IncludeHidden: includeHidden),
            cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Deliberately NOT run through the pipeline, and the reason is the token. The pipeline hands its
    /// call a token that its timeout owns and that is released when the call returns, while the
    /// stream this returns stays open for the length of a download and must be bound to the caller's
    /// own token. A download is also not an operation with a duration the operation timeout could
    /// judge. The inner client answers a transport that gave up as a typed failure itself, as the log
    /// tail does, so the call is still never an anonymous 500.
    /// </remarks>
    public Task<Result<OpenedFileRead>> ReadFileAsync(
        string accountUsername,
        string path,
        ulong maxBytes,
        CancellationToken cancellationToken)
    {
        return _inner.ReadFileAsync(accountUsername, path, maxBytes, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result> CreateDirectoryAsync(
        string accountUsername,
        string path,
        uint mode,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.CreateDirectoryAsync(
                    state.AccountUsername, state.Path, state.Mode, token);
            },
            (Client: _inner, AccountUsername: accountUsername, Path: path, Mode: mode),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result> CreateFileAsync(
        string accountUsername,
        string path,
        uint mode,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.CreateFileAsync(
                    state.AccountUsername, state.Path, state.Mode, token);
            },
            (Client: _inner, AccountUsername: accountUsername, Path: path, Mode: mode),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result> MoveEntryAsync(
        string accountUsername,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.MoveEntryAsync(
                    state.AccountUsername, state.SourcePath, state.DestinationPath, token);
            },
            (
                Client: _inner,
                AccountUsername: accountUsername,
                SourcePath: sourcePath,
                DestinationPath: destinationPath),
            cancellationToken);
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
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.CopyEntryAsync(
                    state.AccountUsername,
                    state.SourcePath,
                    state.DestinationPath,
                    state.MaxEntries,
                    state.MaxBytes,
                    token);
            },
            (
                Client: _inner,
                AccountUsername: accountUsername,
                SourcePath: sourcePath,
                DestinationPath: destinationPath,
                MaxEntries: maxEntries,
                MaxBytes: maxBytes),
            cancellationToken);
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
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.ChangePermissionsAsync(
                    state.AccountUsername, state.Path, state.Mode, state.Recursive, state.DirectoryMode, token);
            },
            (
                Client: _inner,
                AccountUsername: accountUsername,
                Path: path,
                Mode: mode,
                Recursive: recursive,
                DirectoryMode: directoryMode),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<string>> TrashEntryAsync(
        string accountUsername,
        string path,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.TrashEntryAsync(
                    state.AccountUsername, state.Path, token);
            },
            (Client: _inner, AccountUsername: accountUsername, Path: path),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<TrashedEntryDto>>> ListTrashAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.ListTrashAsync(
                    state.AccountUsername, token);
            },
            (Client: _inner, AccountUsername: accountUsername),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<string>> RestoreTrashEntryAsync(
        string accountUsername,
        string trashId,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.RestoreTrashEntryAsync(
                    state.AccountUsername, state.TrashId, state.DestinationPath, token);
            },
            (Client: _inner, AccountUsername: accountUsername, TrashId: trashId, DestinationPath: destinationPath),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<ulong>> EmptyTrashAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.EmptyTrashAsync(
                    state.AccountUsername, token);
            },
            (Client: _inner, AccountUsername: accountUsername),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<ulong>> PurgeExpiredTrashAsync(
        string accountUsername,
        uint olderThanDays,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.PurgeExpiredTrashAsync(
                    state.AccountUsername, state.OlderThanDays, token);
            },
            (Client: _inner, AccountUsername: accountUsername, OlderThanDays: olderThanDays),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result> ForceDeleteEntryAsync(
        string accountUsername,
        string path,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.ForceDeleteEntryAsync(
                    state.AccountUsername, state.Path, token);
            },
            (Client: _inner, AccountUsername: accountUsername, Path: path),
            cancellationToken);
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
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.CreateArchiveAsync(
                    state.AccountUsername,
                    state.SourcePaths,
                    state.DestinationPath,
                    state.Format,
                    state.MaxEntries,
                    state.MaxSourceBytes,
                    token);
            },
            (
                Client: _inner,
                AccountUsername: accountUsername,
                SourcePaths: sourcePaths,
                DestinationPath: destinationPath,
                Format: format,
                MaxEntries: maxEntries,
                MaxSourceBytes: maxSourceBytes),
            cancellationToken);
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
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.ExtractArchiveAsync(
                    state.AccountUsername,
                    state.ArchivePath,
                    state.DestinationPath,
                    state.Format,
                    state.MaxEntries,
                    state.MaxExpandedBytes,
                    state.MaxRatio,
                    token);
            },
            (
                Client: _inner,
                AccountUsername: accountUsername,
                ArchivePath: archivePath,
                DestinationPath: destinationPath,
                Format: format,
                MaxEntries: maxEntries,
                MaxExpandedBytes: maxExpandedBytes,
                MaxRatio: maxRatio),
            cancellationToken);
    }
}
