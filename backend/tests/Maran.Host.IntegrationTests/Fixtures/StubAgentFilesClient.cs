using System.Collections.Concurrent;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.FilesService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Stands in for the agent while the panel's own file-manager path is exercised end to end: the real
/// controllers, the real account resolution, the real validators and the real result translation, over
/// real HTTP and real PostgreSQL.
/// </summary>
/// <remarks>
/// Only the agent is replaced, because it is a separate root process that cannot be present. The Files
/// module keeps no rows, so what this double RECORDS matters as much as what it answers: the system user
/// names it was addressed by are the evidence that no request reached another tenant's home, and an empty
/// record is the evidence that a refused request never reached the agent at all.
///
/// It lists one small tree — a file, a folder and an archive in the home — plus every name a create, a
/// rename, a move or an upload has put there since, so the panel's read-back of an entry it just changed
/// finds it. It cannot answer a file read: the agent's read stream is built from a gRPC response, so a read
/// ends in a busy refusal, which is distinct from the not-found a foreign account gets.
/// </remarks>
public sealed class StubAgentFilesClient : IAgentFilesClient
{
    /// <summary>The file the listing always holds.</summary>
    public const string ExistingFile = "index.html";

    /// <summary>The archive the listing always holds.</summary>
    public const string ExistingArchive = "backup.zip";

    /// <summary>The trash id this double reports for the one entry it pretends to hold in the trash.</summary>
    public const string TrashId = "1-0123456789abcdef";

    /// <summary>Every account user name this client was addressed by, in order, across every call.</summary>
    private readonly ConcurrentQueue<string> _addressed = new();

    /// <summary>The names a change has put into the listing since the last <see cref="Reset"/>.</summary>
    private readonly ConcurrentQueue<string> _created = new();

    /// <summary>Gets every account user name this client was addressed by, in order, across every call.</summary>
    public IReadOnlyList<string> AddressedAccounts
    {
        get
        {
            return [.. _addressed];
        }
    }

    /// <summary>Forgets what was recorded, so the next request starts from a clean record.</summary>
    public void Reset()
    {
        _addressed.Clear();
        _created.Clear();
    }

    /// <inheritdoc />
    public Task<Result<ulong>> WriteFileAsync(
        string accountUsername,
        string path,
        string content,
        uint mode,
        CancellationToken cancellationToken)
    {
        Touch(accountUsername);
        return Task.FromResult(Result<ulong>.Ok((ulong)content.Length));
    }

    /// <inheritdoc />
    public Task<Result> DeleteEntryAsync(
        string accountUsername,
        string path,
        bool recursive,
        CancellationToken cancellationToken)
    {
        Touch(accountUsername);
        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
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
        Touch(accountUsername);

        var entries = new List<FileEntryDto>
        {
            new(ExistingFile, FileEntryKind.File, 5, 0b110_100_100, DateTimeOffset.UnixEpoch, 1000, 1000, 1),
            new("docs", FileEntryKind.Directory, 4096, 0b111_101_101, DateTimeOffset.UnixEpoch, 1000, 1000, 2),
            new(ExistingArchive, FileEntryKind.File, 10, 0b110_100_100, DateTimeOffset.UnixEpoch, 1000, 1000, 1),
        };
        foreach (var name in _created.Distinct())
        {
            entries.Add(new FileEntryDto(name, FileEntryKind.File, 5, 0b110_100_100, DateTimeOffset.UnixEpoch, 1000, 1000, 1));
        }

        return Task.FromResult(Result<DirectoryListingDto>.Ok(new DirectoryListingDto(entries, (uint)entries.Count, 0)));
    }

    /// <inheritdoc />
    public Task<Result<FileSearchResultDto>> SearchEntriesAsync(
        string accountUsername,
        string path,
        string query,
        uint maxDepth,
        uint maxResults,
        bool includeHidden,
        CancellationToken cancellationToken)
    {
        Touch(accountUsername);

        var match = new FileSearchMatchDto(
            ExistingFile,
            new FileEntryDto(ExistingFile, FileEntryKind.File, 5, 0b110_100_100, DateTimeOffset.UnixEpoch, 1000, 1000, 1));
        return Task.FromResult(Result<FileSearchResultDto>.Ok(new FileSearchResultDto([match], false, 0)));
    }

    /// <inheritdoc />
    public Task<Result<OpenedFileRead>> ReadFileAsync(
        string accountUsername,
        string path,
        ulong maxBytes,
        CancellationToken cancellationToken)
    {
        Touch(accountUsername);
        return Task.FromResult(
            Result<OpenedFileRead>.Fail(Error.Of("AgentSaturated", ErrorType.Unavailable)));
    }

    /// <summary>Writes a request body to the agent as a stream, consuming it.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="path">Destination path.</param>
    /// <param name="mode">Permission bits for the file.</param>
    /// <param name="body">The bytes to write.</param>
    /// <param name="maxBytes">The most bytes the write may take.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The bytes consumed.</returns>
    public async Task<Result<ulong>> WriteFileStreamAsync(
        string accountUsername,
        string path,
        uint mode,
        Stream body,
        ulong maxBytes,
        CancellationToken cancellationToken)
    {
        Touch(accountUsername);
        Remember(path);
        using var sink = new MemoryStream();
        await body.CopyToAsync(sink, cancellationToken);
        _created.Enqueue(path);
        return Result<ulong>.Ok((ulong)sink.Length);
    }

    /// <inheritdoc />
    public Task<Result> CreateDirectoryAsync(
        string accountUsername,
        string path,
        uint mode,
        CancellationToken cancellationToken)
    {
        Touch(accountUsername);
        Remember(path);
        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result> CreateFileAsync(
        string accountUsername,
        string path,
        uint mode,
        CancellationToken cancellationToken)
    {
        Touch(accountUsername);
        Remember(path);
        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result> MoveEntryAsync(
        string accountUsername,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        Touch(accountUsername);
        Remember(destinationPath);
        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result> CopyEntryAsync(
        string accountUsername,
        string sourcePath,
        string destinationPath,
        ulong maxEntries,
        ulong maxBytes,
        CancellationToken cancellationToken)
    {
        Touch(accountUsername);
        Remember(destinationPath);
        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result<PermissionChangeDto>> ChangePermissionsAsync(
        string accountUsername,
        string path,
        uint mode,
        bool recursive,
        uint directoryMode,
        CancellationToken cancellationToken)
    {
        Touch(accountUsername);
        return Task.FromResult(Result<PermissionChangeDto>.Ok(new PermissionChangeDto(1, 0)));
    }

    /// <inheritdoc />
    public Task<Result<string>> TrashEntryAsync(
        string accountUsername,
        string path,
        CancellationToken cancellationToken)
    {
        Touch(accountUsername);
        return Task.FromResult(Result<string>.Ok(TrashId));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<TrashedEntryDto>>> ListTrashAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        Touch(accountUsername);
        IReadOnlyList<TrashedEntryDto> trashed =
            [new TrashedEntryDto(TrashId, ExistingFile, DateTimeOffset.UnixEpoch, false, 5)];
        return Task.FromResult(Result<IReadOnlyList<TrashedEntryDto>>.Ok(trashed));
    }

    /// <inheritdoc />
    public Task<Result<string>> RestoreTrashEntryAsync(
        string accountUsername,
        string trashId,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        Touch(accountUsername);
        return Task.FromResult(Result<string>.Ok(ExistingFile));
    }

    /// <inheritdoc />
    public Task<Result<ulong>> EmptyTrashAsync(string accountUsername, CancellationToken cancellationToken)
    {
        Touch(accountUsername);
        return Task.FromResult(Result<ulong>.Ok(1));
    }

    /// <inheritdoc />
    public Task<Result<ulong>> PurgeExpiredTrashAsync(
        string accountUsername,
        uint olderThanDays,
        CancellationToken cancellationToken)
    {
        Touch(accountUsername);
        return Task.FromResult(Result<ulong>.Ok(0));
    }

    /// <inheritdoc />
    public Task<Result> ForceDeleteEntryAsync(
        string accountUsername,
        string path,
        CancellationToken cancellationToken)
    {
        Touch(accountUsername);
        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result<ulong>> CreateArchiveAsync(
        string accountUsername,
        IReadOnlyList<string> sourcePaths,
        string destinationPath,
        AgentArchiveFormat format,
        ulong maxEntries,
        ulong maxSourceBytes,
        CancellationToken cancellationToken)
    {
        Touch(accountUsername);
        return Task.FromResult(Result<ulong>.Ok(10));
    }

    /// <inheritdoc />
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
        Touch(accountUsername);
        return Task.FromResult(Result<uint>.Ok(3));
    }

    /// <summary>Records a call.</summary>
    /// <param name="accountUsername">The account user name the call was addressed to.</param>
    private void Touch(string accountUsername)
    {
        _addressed.Enqueue(accountUsername);
    }

    /// <summary>Remembers a name a change put into the home, so the listing shows it from then on.</summary>
    /// <param name="path">The path the change created.</param>
    private void Remember(string path)
    {
        _created.Enqueue(path[(path.LastIndexOf('/') + 1)..]);
    }
}
