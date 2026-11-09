using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.FilesService;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.Resilience;

/// <summary>
/// An inner files client whose calls cannot finish until a stated number of them are inside it at
/// once, so a caller that serialises them deadlocks instead of passing.
/// </summary>
/// <remarks>
/// <para>
/// This is the positive control the overlap test needs, built into the fake rather than asserted
/// afterwards. A test that merely counted calls would be satisfied by two calls that ran one after
/// the other, which is exactly the state it exists to distinguish from — so the second caller's
/// arrival is made the precondition of the FIRST caller's return. Both are then provably inside the
/// agent client at the same instant, or neither ever returns and the test's own deadline fails it.
/// </para>
/// <para>
/// <see cref="PeakConcurrency"/> is recorded rather than inferred, because "they both entered" and
/// "they were both inside at once" are different claims and only the second is the property.
/// </para>
/// </remarks>
internal sealed class GatedAgentFilesClient : IAgentFilesClient
{
    /// <summary>How many callers must be inside before any of them is allowed to return.</summary>
    private readonly int _expected;

    /// <summary>Completed once <see cref="_expected"/> callers are inside.</summary>
    private readonly TaskCompletionSource _gateOpened =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>How many callers are inside right now.</summary>
    private int _inside;

    /// <summary>The most callers that were ever inside at the same time.</summary>
    private int _peak;

    /// <summary>The most callers that were ever inside this client at the same time.</summary>
    public int PeakConcurrency
    {
        get
        {
            return Volatile.Read(ref _peak);
        }
    }

    /// <summary>Creates a client that holds its callers until <paramref name="expected"/> are inside.</summary>
    /// <param name="expected">How many concurrent callers open the gate.</param>
    public GatedAgentFilesClient(int expected)
    {
        _expected = expected;
    }

    /// <inheritdoc/>
    public async Task<Result<ulong>> WriteFileAsync(
        string accountUsername,
        string path,
        string content,
        uint mode,
        CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken);

        return Result<ulong>.Ok((ulong)content.Length);
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

    /// <inheritdoc/>
    public async Task<Result> DeleteEntryAsync(
        string accountUsername,
        string path,
        bool recursive,
        CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken);

        return Result.Ok();
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

    /// <summary>Records this caller's arrival and waits for the gate the last arrival opens.</summary>
    /// <param name="cancellationToken">The token the pipeline cancels when its timeout fires.</param>
    /// <returns>A task that completes once enough callers are inside.</returns>
    private async Task EnterAsync(CancellationToken cancellationToken)
    {
        var inside = Interlocked.Increment(ref _inside);

        // Raised under a compare-and-swap loop rather than a plain assignment: two arrivals racing
        // here would otherwise both read the old peak and both write their own, and the larger one
        // could be overwritten by the smaller.
        var seen = Volatile.Read(ref _peak);
        while (inside > seen)
        {
            var previous = Interlocked.CompareExchange(ref _peak, inside, seen);
            if (previous == seen)
            {
                break;
            }

            seen = previous;
        }

        if (inside >= _expected)
        {
            _gateOpened.TrySetResult();
        }

        try
        {
            await _gateOpened.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _inside);
        }
    }
}
