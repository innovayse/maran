using System.Text;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.FilesService;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.State;

namespace Maran.Agent.Simulated.Services.FilesService;

/// <summary>
/// The agent's file operations against the simulated server's memory: a file is a string held for
/// the demo's lifetime, written under no uid and to no disk.
/// </summary>
public sealed class SimulatedAgentFilesClient : IAgentFilesClient
{
    /// <summary>The body cap the real agent applies when a caller states none: 1 MiB.</summary>
    private const ulong DefaultMaxBytes = 1024 * 1024;

    /// <summary>The files of the simulated server.</summary>
    private readonly SimulatedFileStore _files;

    /// <summary>Creates a client over the shared simulated state.</summary>
    /// <param name="ledger">The simulated server's state.</param>
    public SimulatedAgentFilesClient(SimulatedAgentLedger ledger)
    {
        _files = ledger.Files;
    }

    /// <inheritdoc/>
    public Task<Result<ulong>> WriteFileAsync(
        string accountUsername,
        string path,
        string content,
        uint mode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_files.Write(accountUsername, path, content, mode));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The simulated server holds a file as a string, so the body is decoded as UTF-8 text; a binary
    /// body is stored lossily, which no demo flow depends on. The cap is applied as the real agent
    /// applies it: a body above it is refused and nothing is stored.
    /// </remarks>
    public async Task<Result<ulong>> WriteFileStreamAsync(
        string accountUsername,
        string path,
        uint mode,
        Stream body,
        ulong maxBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cap = maxBytes == 0 ? DefaultMaxBytes : maxBytes;
        using var received = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if ((ulong)received.Length + (ulong)read > cap)
            {
                return Result<ulong>.Fail(SimulatedAgentErrors.InvalidInput());
            }

            received.Write(chunk, 0, read);
        }

        return _files.Write(accountUsername, path, Encoding.UTF8.GetString(received.ToArray()), mode);
    }

    /// <inheritdoc/>
    public Task<Result> DeleteEntryAsync(
        string accountUsername,
        string path,
        bool recursive,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_files.Delete(accountUsername, path, recursive));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Browsing is not modelled by the simulated server yet: its files carry no modification time, no
    /// owner and no link count to describe, so it answers as an agent build that does not serve the
    /// rpc does, with a typed refusal, rather than inventing the metadata.
    /// </remarks>
    public Task<Result<DirectoryListingDto>> ListDirectoryAsync(
        string accountUsername,
        string path,
        uint offset,
        uint limit,
        bool includeHidden,
        DirectorySortOrder sort,
        bool descending,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<DirectoryListingDto>.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    /// <remarks>See <see cref="ListDirectoryAsync"/>: the simulated server does not model browsing yet.</remarks>
    public Task<Result<FileSearchResultDto>> SearchEntriesAsync(
        string accountUsername,
        string path,
        string query,
        uint maxDepth,
        uint maxResults,
        bool includeHidden,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<FileSearchResultDto>.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// See <see cref="ListDirectoryAsync"/>: reading waits for the same task that models the files'
    /// metadata, so a listing and a read of one file agree.
    /// </remarks>
    public Task<Result<OpenedFileRead>> ReadFileAsync(
        string accountUsername,
        string path,
        ulong maxBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<OpenedFileRead>.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    /// <remarks>See <see cref="ListDirectoryAsync"/>: the simulated server does not model this operation.</remarks>
    public Task<Result> CreateDirectoryAsync(
        string accountUsername,
        string path,
        uint mode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    /// <remarks>See <see cref="ListDirectoryAsync"/>: the simulated server does not model this operation.</remarks>
    public Task<Result> CreateFileAsync(
        string accountUsername,
        string path,
        uint mode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    /// <remarks>See <see cref="ListDirectoryAsync"/>: the simulated server does not model this operation.</remarks>
    public Task<Result> MoveEntryAsync(
        string accountUsername,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    /// <remarks>See <see cref="ListDirectoryAsync"/>: the simulated server does not model this operation.</remarks>
    public Task<Result> CopyEntryAsync(
        string accountUsername,
        string sourcePath,
        string destinationPath,
        ulong maxEntries,
        ulong maxBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    /// <remarks>See <see cref="ListDirectoryAsync"/>: the simulated server does not model this operation.</remarks>
    public Task<Result<PermissionChangeDto>> ChangePermissionsAsync(
        string accountUsername,
        string path,
        uint mode,
        bool recursive,
        uint directoryMode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<PermissionChangeDto>.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    /// <remarks>See <see cref="ListDirectoryAsync"/>: the simulated server does not model this operation.</remarks>
    public Task<Result<string>> TrashEntryAsync(
        string accountUsername,
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<string>.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    /// <remarks>See <see cref="ListDirectoryAsync"/>: the simulated server does not model this operation.</remarks>
    public Task<Result<IReadOnlyList<TrashedEntryDto>>> ListTrashAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<IReadOnlyList<TrashedEntryDto>>.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    /// <remarks>See <see cref="ListDirectoryAsync"/>: the simulated server does not model this operation.</remarks>
    public Task<Result<string>> RestoreTrashEntryAsync(
        string accountUsername,
        string trashId,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<string>.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    /// <remarks>See <see cref="ListDirectoryAsync"/>: the simulated server does not model this operation.</remarks>
    public Task<Result<ulong>> EmptyTrashAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<ulong>.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    /// <remarks>See <see cref="ListDirectoryAsync"/>: the simulated server does not model this operation.</remarks>
    public Task<Result<ulong>> PurgeExpiredTrashAsync(
        string accountUsername,
        uint olderThanDays,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<ulong>.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    /// <remarks>See <see cref="ListDirectoryAsync"/>: the simulated server does not model this operation.</remarks>
    public Task<Result> ForceDeleteEntryAsync(
        string accountUsername,
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    /// <remarks>See <see cref="ListDirectoryAsync"/>: the simulated server does not model this operation.</remarks>
    public Task<Result<ulong>> CreateArchiveAsync(
        string accountUsername,
        IReadOnlyList<string> sourcePaths,
        string destinationPath,
        AgentArchiveFormat format,
        ulong maxEntries,
        ulong maxSourceBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<ulong>.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    /// <remarks>See <see cref="ListDirectoryAsync"/>: the simulated server does not model this operation.</remarks>
    public Task<Result<uint>> ExtractArchiveAsync(
        string accountUsername,
        string archivePath,
        string destinationPath,
        AgentArchiveFormat format,
        ulong maxEntries,
        ulong maxExpandedBytes,
        uint maxRatio,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<uint>.Fail(SimulatedAgentErrors.NotImplemented()));
    }
}
