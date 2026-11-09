using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.FilesService;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// An <see cref="IAgentFilesClient"/> double recording every file the panel asked the agent to write
/// or delete on a customer's behalf.
/// </summary>
public sealed class RecordingAgentFilesClient : IAgentFilesClient
{
    /// <summary>The refusal to answer writes with, or null to succeed.</summary>
    private readonly Error? _writeFailure;

    /// <summary>Every write, in order: whose home, which path, what content, and with what mode.</summary>
    public List<(string Account, string Path, string Content, uint Mode)> Writes { get; } = [];

    /// <summary>Every delete, in order.</summary>
    public List<(string Account, string Path, bool Recursive)> Deletes { get; } = [];

    /// <summary>Creates a client whose writes succeed, or one whose writes refuse.</summary>
    /// <param name="writeFailure">The refusal to answer writes with, or null to succeed.</param>
    public RecordingAgentFilesClient(Error? writeFailure = null)
    {
        _writeFailure = writeFailure;
    }

    /// <inheritdoc />
    public Task<Result<ulong>> WriteFileAsync(
        string accountUsername,
        string path,
        string content,
        uint mode,
        CancellationToken cancellationToken)
    {
        Writes.Add((accountUsername, path, content, mode));

        return Task.FromResult(_writeFailure is null
            ? Result<ulong>.Ok((ulong)content.Length)
            : Result<ulong>.Fail(_writeFailure));
    }

    /// <inheritdoc/>
    public Task<Result<ulong>> WriteFileStreamAsync(
        string accountUsername,
        string path,
        uint mode,
        Stream body,
        ulong maxBytes,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double does not model streamed uploads.");
    }

    /// <inheritdoc />
    public Task<Result> DeleteEntryAsync(
        string accountUsername,
        string path,
        bool recursive,
        CancellationToken cancellationToken)
    {
        Deletes.Add((accountUsername, path, recursive));
        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc/>
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
        throw new NotSupportedException("This double does not model browsing or reading.");
    }

    /// <inheritdoc/>
    public Task<Result<FileSearchResultDto>> SearchEntriesAsync(
        string accountUsername,
        string path,
        string query,
        uint maxDepth,
        uint maxResults,
        bool includeHidden,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double does not model browsing or reading.");
    }

    /// <inheritdoc/>
    public Task<Result<OpenedFileRead>> ReadFileAsync(
        string accountUsername,
        string path,
        ulong maxBytes,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double does not model browsing or reading.");
    }

    /// <inheritdoc/>
    public Task<Result> CreateDirectoryAsync(
        string accountUsername,
        string path,
        uint mode,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double does not model changing a customer's tree.");
    }

    /// <inheritdoc/>
    public Task<Result> CreateFileAsync(
        string accountUsername,
        string path,
        uint mode,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double does not model changing a customer's tree.");
    }

    /// <inheritdoc/>
    public Task<Result> MoveEntryAsync(
        string accountUsername,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double does not model changing a customer's tree.");
    }

    /// <inheritdoc/>
    public Task<Result> CopyEntryAsync(
        string accountUsername,
        string sourcePath,
        string destinationPath,
        ulong maxEntries,
        ulong maxBytes,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double does not model changing a customer's tree.");
    }

    /// <inheritdoc/>
    public Task<Result<PermissionChangeDto>> ChangePermissionsAsync(
        string accountUsername,
        string path,
        uint mode,
        bool recursive,
        uint directoryMode,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double does not model changing a customer's tree.");
    }

    /// <inheritdoc/>
    public Task<Result<string>> TrashEntryAsync(
        string accountUsername,
        string path,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double does not model changing a customer's tree.");
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<TrashedEntryDto>>> ListTrashAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double does not model changing a customer's tree.");
    }

    /// <inheritdoc/>
    public Task<Result<string>> RestoreTrashEntryAsync(
        string accountUsername,
        string trashId,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double does not model changing a customer's tree.");
    }

    /// <inheritdoc/>
    public Task<Result<ulong>> EmptyTrashAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double does not model changing a customer's tree.");
    }

    /// <inheritdoc/>
    public Task<Result<ulong>> PurgeExpiredTrashAsync(
        string accountUsername,
        uint olderThanDays,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double does not model changing a customer's tree.");
    }

    /// <inheritdoc/>
    public Task<Result> ForceDeleteEntryAsync(
        string accountUsername,
        string path,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double does not model changing a customer's tree.");
    }

    /// <inheritdoc/>
    public Task<Result<ulong>> CreateArchiveAsync(
        string accountUsername,
        IReadOnlyList<string> sourcePaths,
        string destinationPath,
        AgentArchiveFormat format,
        ulong maxEntries,
        ulong maxSourceBytes,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double does not model changing a customer's tree.");
    }

    /// <inheritdoc/>
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
        throw new NotSupportedException("This double does not model changing a customer's tree.");
    }
}
