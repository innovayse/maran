using System.Text;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.FilesService;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Files.Tests.TestSupport;

/// <summary>
/// The one agent files client every Files test uses: it answers from folders and files a test lays out,
/// records every call it receives, and fails a call on request, so a test can say both what a handler
/// returned and what the agent saw — as importantly, that it saw nothing at all.
/// </summary>
/// <remarks>
/// It lists the way the agent does where a handler depends on it: folders before other entries, a
/// byte-wise name order, an offset and a limit (0 meaning no limit). It does not pretend to be the agent
/// anywhere else: a read serves the bytes a test put beside the entry, and every other operation records
/// itself and answers success, or the failure scripted for it.
/// </remarks>
public sealed class RecordingAgentFilesClient : IAgentFilesClient
{
    /// <summary>The failures scripted so far: the operation, what it applies to, and the error.</summary>
    private readonly List<(string Operation, string Subject, Error Error)> _failures = [];

    /// <summary>The entries of each folder, by the folder's path below the home ("" is the home).</summary>
    private readonly Dictionary<string, List<FileEntryDto>> _folders = [];

    /// <summary>The bytes of each file, by its path below the home.</summary>
    private readonly Dictionary<string, byte[]> _contents = [];

    /// <summary>Gets every call made so far, in order.</summary>
    public List<AgentFilesCall> Calls { get; } = [];

    /// <summary>Gets every directory listing asked for, in order.</summary>
    public List<AgentListRequest> ListRequests { get; } = [];

    /// <summary>Gets every search asked for, in order.</summary>
    public List<AgentSearchRequest> SearchRequests { get; } = [];

    /// <summary>Gets every whole-text write received.</summary>
    public List<(string Path, string Content, uint Mode)> TextWrites { get; } = [];

    /// <summary>Gets every streamed write received, with the bytes it carried.</summary>
    public List<(string Path, uint Mode, byte[] Body, ulong MaxBytes)> StreamedWrites { get; } = [];

    /// <summary>Gets every permission change received: the path, the mode, whether it descended and the folders' mode.</summary>
    public List<(string Path, uint Mode, bool Recursive, uint DirectoryMode)> PermissionChanges { get; } = [];

    /// <summary>Gets every expired-trash purge asked for: the account and the age, in days, past which entries go.</summary>
    public List<(string Account, uint Days)> PurgeRequests { get; } = [];

    /// <summary>Gets the matches a search answers with.</summary>
    public List<FileSearchMatchDto> SearchMatches { get; } = [];

    /// <summary>Gets or sets whether a search answers that a bound stopped it.</summary>
    public bool SearchTruncated { get; set; }

    /// <summary>Gets the entries a trash listing answers.</summary>
    public List<TrashedEntryDto> TrashAnswer { get; } = [];

    /// <summary>Gets or sets how many entries emptying the trash reports it removed.</summary>
    public ulong EmptiedCount { get; set; } = 3;

    /// <summary>Gets or sets how many entries an expired-trash purge reports it removed.</summary>
    public ulong PurgedCount { get; set; } = 2;

    /// <summary>Gets or sets how many bytes an archive reports it wrote.</summary>
    public ulong ArchiveBytes { get; set; } = 2048;

    /// <summary>Gets or sets how many entries an extraction reports it created.</summary>
    public uint ExtractedEntries { get; set; } = 7;

    /// <summary>Gets or sets a refusal that every call answers with instead of its result.</summary>
    public Error? Refusal { get; set; }

    /// <summary>Gets or sets a refusal that a write answers with, while every other call answers normally.</summary>
    public Error? WriteRefusal { get; set; }

    /// <summary>Gets or sets whether a read opens a stream that ends with the agent's refusal after its first chunk.</summary>
    public bool ReadsFailHalfWay { get; set; }

    /// <summary>Gets or sets how many bytes a read delivers, as a file truncated meanwhile would; null for all.</summary>
    public int? ReadsDeliverOnly { get; set; }

    /// <summary>Gets or sets an exception a streamed write throws instead of answering.</summary>
    public Exception? StreamedWriteThrows { get; set; }

    /// <summary>Makes an operation fail on one subject, or on every call when the subject is empty.</summary>
    /// <param name="operation">The client method's name.</param>
    /// <param name="subject">A path, an account user name, or an empty string for every call.</param>
    /// <param name="error">The agent's failure.</param>
    public void FailOn(string operation, string subject, Error error)
    {
        _failures.Add((operation, subject, error));
    }

    /// <summary>Puts an entry into a folder of the tree.</summary>
    /// <param name="folder">The folder's path below the home; empty for the home.</param>
    /// <param name="entry">The entry the folder lists.</param>
    /// <param name="content">The bytes a read returns; null for an entry that is not a readable file.</param>
    public void Put(string folder, FileEntryDto entry, byte[]? content = null)
    {
        if (!_folders.TryGetValue(folder, out var entries))
        {
            entries = [];
            _folders[folder] = entries;
        }

        entries.Add(entry);
        if (content is not null)
        {
            _contents[folder.Length == 0 ? entry.Name : folder + "/" + entry.Name] = content;
        }
    }

    /// <summary>Puts a regular file into a directory of the tree.</summary>
    /// <param name="directory">The directory path; "" is the home.</param>
    /// <param name="name">The file name.</param>
    /// <param name="size">Its size in bytes.</param>
    /// <param name="mode">Its permission bits.</param>
    /// <param name="modifiedAt">When it was last modified.</param>
    public void AddFile(string directory, string name, ulong size, uint mode, DateTimeOffset modifiedAt)
    {
        Put(directory, new FileEntryDto(name, FileEntryKind.File, size, mode, modifiedAt, 1000, 1000, 1));
    }

    /// <summary>Puts a subdirectory into a directory of the tree.</summary>
    /// <param name="directory">The directory path; "" is the home.</param>
    /// <param name="name">The subdirectory name.</param>
    public void AddDirectory(string directory, string name)
    {
        Put(
            directory,
            new FileEntryDto(name, FileEntryKind.Directory, 4096, 0b111_101_101, DateTimeOffset.UnixEpoch, 1000, 1000, 2));
    }

    /// <inheritdoc />
    public Task<Result<ulong>> WriteFileAsync(
        string accountUsername,
        string path,
        string content,
        uint mode,
        CancellationToken cancellationToken)
    {
        TextWrites.Add((path, content, mode));
        var size = (ulong)Encoding.UTF8.GetByteCount(content);
        var written = Valued(nameof(WriteFileAsync), accountUsername, path, size, null, mode);
        if (written.IsSuccess)
        {
            EnsureFile(path, size, mode);
        }

        return Task.FromResult(written);
    }

    /// <inheritdoc />
    public async Task<Result<ulong>> WriteFileStreamAsync(
        string accountUsername,
        string path,
        uint mode,
        Stream body,
        ulong maxBytes,
        CancellationToken cancellationToken)
    {
        Calls.Add(new AgentFilesCall(nameof(WriteFileStreamAsync), accountUsername, path, null, mode));
        if (StreamedWriteThrows is not null)
        {
            throw StreamedWriteThrows;
        }

        using var buffer = new MemoryStream();
        await body.CopyToAsync(buffer, cancellationToken);
        StreamedWrites.Add((path, mode, buffer.ToArray(), maxBytes));

        var failure = FailureOf(nameof(WriteFileStreamAsync), accountUsername, path);
        return failure is not null ? Result<ulong>.Fail(failure) : Result<ulong>.Ok((ulong)buffer.Length);
    }

    /// <inheritdoc />
    public Task<Result> DeleteEntryAsync(
        string accountUsername,
        string path,
        bool recursive,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Plain(nameof(DeleteEntryAsync), accountUsername, path));
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
        ListRequests.Add(new AgentListRequest(path, offset, limit, includeHidden, sort, descending));
        Calls.Add(new AgentFilesCall(nameof(ListDirectoryAsync), accountUsername, path));

        var failure = FailureOf(nameof(ListDirectoryAsync), accountUsername, path);
        if (failure is not null)
        {
            return Task.FromResult(Result<DirectoryListingDto>.Fail(failure));
        }

        if (!_folders.TryGetValue(path, out var all))
        {
            return Task.FromResult(
                Result<DirectoryListingDto>.Fail(Error.Of("AgentNotFound", ErrorType.NotFound)));
        }

        var visible = all.Where(entry => { return includeHidden || !entry.Name.StartsWith('.'); }).ToList();
        var ordered = Ordered(visible, sort, descending).ToList();
        var page = ordered
            .Skip((int)Math.Min(offset, int.MaxValue))
            .Take(limit == 0 ? int.MaxValue : (int)limit)
            .ToList();

        return Task.FromResult(Result<DirectoryListingDto>.Ok(new DirectoryListingDto(page, (uint)ordered.Count, 0)));
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
        SearchRequests.Add(new AgentSearchRequest(path, query, maxDepth, maxResults, includeHidden));

        return Task.FromResult(Valued(
            nameof(SearchEntriesAsync),
            accountUsername,
            path,
            new FileSearchResultDto([.. SearchMatches], SearchTruncated, 0)));
    }

    /// <inheritdoc />
    public async Task<Result<OpenedFileRead>> ReadFileAsync(
        string accountUsername,
        string path,
        ulong maxBytes,
        CancellationToken cancellationToken)
    {
        Calls.Add(new AgentFilesCall(nameof(ReadFileAsync), accountUsername, path));
        var failure = FailureOf(nameof(ReadFileAsync), accountUsername, path);
        if (failure is not null)
        {
            return Result<OpenedFileRead>.Fail(failure);
        }

        if (!_contents.TryGetValue(path, out var content))
        {
            return Result<OpenedFileRead>.Fail(Error.Of("AgentNotFound", ErrorType.NotFound));
        }

        if (ReadsFailHalfWay)
        {
            return Result<OpenedFileRead>.Ok(await OpenedFileReads.FailingAfterAsync(content[..(content.Length / 2)]));
        }

        var delivered = ReadsDeliverOnly is { } only ? content[..Math.Min(only, content.Length)] : content;
        return Result<OpenedFileRead>.Ok(await OpenedFileReads.OfAsync(delivered));
    }

    /// <inheritdoc />
    public Task<Result> CreateDirectoryAsync(
        string accountUsername,
        string path,
        uint mode,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Plain(nameof(CreateDirectoryAsync), accountUsername, path, null, mode));
    }

    /// <inheritdoc />
    public Task<Result> CreateFileAsync(
        string accountUsername,
        string path,
        uint mode,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Plain(nameof(CreateFileAsync), accountUsername, path, null, mode));
    }

    /// <inheritdoc />
    public Task<Result> MoveEntryAsync(
        string accountUsername,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Plain(nameof(MoveEntryAsync), accountUsername, sourcePath, destinationPath));
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
        return Task.FromResult(Plain(nameof(CopyEntryAsync), accountUsername, sourcePath, destinationPath));
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
        PermissionChanges.Add((path, mode, recursive, directoryMode));
        return Task.FromResult(
            Valued(nameof(ChangePermissionsAsync), accountUsername, path, new PermissionChangeDto(1, 0), null, mode));
    }

    /// <inheritdoc />
    public Task<Result<string>> TrashEntryAsync(
        string accountUsername,
        string path,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Valued(nameof(TrashEntryAsync), accountUsername, path, "1-0123456789abcdef"));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<TrashedEntryDto>>> ListTrashAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            Valued<IReadOnlyList<TrashedEntryDto>>(nameof(ListTrashAsync), accountUsername, string.Empty, [.. TrashAnswer]));
    }

    /// <inheritdoc />
    public Task<Result<string>> RestoreTrashEntryAsync(
        string accountUsername,
        string trashId,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Valued(nameof(RestoreTrashEntryAsync), accountUsername, trashId, "restored/path"));
    }

    /// <inheritdoc />
    public Task<Result<ulong>> EmptyTrashAsync(string accountUsername, CancellationToken cancellationToken)
    {
        return Task.FromResult(Valued(nameof(EmptyTrashAsync), accountUsername, string.Empty, EmptiedCount));
    }

    /// <inheritdoc />
    public Task<Result<ulong>> PurgeExpiredTrashAsync(
        string accountUsername,
        uint olderThanDays,
        CancellationToken cancellationToken)
    {
        PurgeRequests.Add((accountUsername, olderThanDays));
        return Task.FromResult(Valued(nameof(PurgeExpiredTrashAsync), accountUsername, string.Empty, PurgedCount));
    }

    /// <inheritdoc />
    public Task<Result> ForceDeleteEntryAsync(
        string accountUsername,
        string path,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Plain(nameof(ForceDeleteEntryAsync), accountUsername, path));
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
        return Task.FromResult(Valued(nameof(CreateArchiveAsync), accountUsername, destinationPath, ArchiveBytes));
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
        return Task.FromResult(
            Valued(nameof(ExtractArchiveAsync), accountUsername, archivePath, ExtractedEntries, destinationPath));
    }

    /// <summary>Makes sure a written file exists in the tree, as a real write leaves it.</summary>
    /// <param name="path">The file's path.</param>
    /// <param name="size">Its size in bytes.</param>
    /// <param name="mode">Its permission bits.</param>
    private void EnsureFile(string path, ulong size, uint mode)
    {
        var cut = path.LastIndexOf('/');
        var directory = cut < 0 ? string.Empty : path[..cut];
        var name = path[(cut + 1)..];
        if (!_folders.TryGetValue(directory, out var entries) || !entries.Any(entry => { return entry.Name == name; }))
        {
            Put(directory, new FileEntryDto(name, FileEntryKind.File, size, mode, DateTimeOffset.UnixEpoch, 1000, 1000, 1));
        }
    }

    /// <summary>Finds the scripted failure that applies to a call.</summary>
    /// <param name="operation">The client method's name.</param>
    /// <param name="account">The account the call was addressed to.</param>
    /// <param name="path">The path of the call.</param>
    /// <returns>The error, or <see langword="null"/> when the call succeeds.</returns>
    private Error? FailureOf(string operation, string account, string path)
    {
        foreach (var (scripted, subject, error) in _failures)
        {
            if (scripted == operation && (subject.Length == 0 || subject == path || subject == account))
            {
                return error;
            }
        }

        if (WriteRefusal is not null && operation is nameof(WriteFileAsync) or nameof(WriteFileStreamAsync))
        {
            return WriteRefusal;
        }

        return Refusal;
    }

    /// <summary>Records a call and answers with success or the scripted failure.</summary>
    /// <param name="operation">The client method's name.</param>
    /// <param name="account">The account addressed.</param>
    /// <param name="path">The primary path.</param>
    /// <param name="destination">The second path, if any.</param>
    /// <param name="mode">The permission bits, if any.</param>
    /// <returns>The result.</returns>
    private Result Plain(string operation, string account, string path, string? destination = null, uint? mode = null)
    {
        Calls.Add(new AgentFilesCall(operation, account, path, destination, mode));
        var failure = FailureOf(operation, account, path);
        return failure is null ? Result.Ok() : Result.Fail(failure);
    }

    /// <summary>Records a call and answers with a value or the scripted failure.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="operation">The client method's name.</param>
    /// <param name="account">The account addressed.</param>
    /// <param name="path">The primary path.</param>
    /// <param name="value">What a success answers.</param>
    /// <param name="destination">The second path, if any.</param>
    /// <param name="mode">The permission bits, if any.</param>
    /// <returns>The result.</returns>
    private Result<T> Valued<T>(
        string operation,
        string account,
        string path,
        T value,
        string? destination = null,
        uint? mode = null)
    {
        Calls.Add(new AgentFilesCall(operation, account, path, destination, mode));
        var failure = FailureOf(operation, account, path);
        return failure is null ? Result<T>.Ok(value) : Result<T>.Fail(failure);
    }

    /// <summary>Orders entries the way the agent does: folders first, then by the chosen key within each group.</summary>
    /// <param name="entries">The entries to order.</param>
    /// <param name="sort">The key.</param>
    /// <param name="descending">Whether the order within each group is reversed.</param>
    /// <returns>The ordered entries.</returns>
    private static IEnumerable<FileEntryDto> Ordered(List<FileEntryDto> entries, DirectorySortOrder sort, bool descending)
    {
        IEnumerable<FileEntryDto> Within(IEnumerable<FileEntryDto> group)
        {
            Func<FileEntryDto, object> key = entry =>
            {
                return sort switch
                {
                    DirectorySortOrder.Size => entry.SizeBytes,
                    DirectorySortOrder.Modified => entry.ModifiedAt,
                    _ => entry.Name,
                };
            };

            return descending
                ? group.OrderByDescending(key, Comparer<object>.Create(Compare))
                : group.OrderBy(key, Comparer<object>.Create(Compare));
        }

        return Within(entries.Where(entry => { return entry.Kind == FileEntryKind.Directory; }))
            .Concat(Within(entries.Where(entry => { return entry.Kind != FileEntryKind.Directory; })));
    }

    /// <summary>Compares two keys of the same kind; names are compared byte-wise, as the agent compares them.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    /// <returns>The comparison.</returns>
    private static int Compare(object? left, object? right)
    {
        return left is string leftName && right is string rightName
            ? string.CompareOrdinal(leftName, rightName)
            : Comparer<object>.Default.Compare(left, right);
    }
}
