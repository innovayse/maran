using Maran.Agent.Client.Services.FilesService;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// The panel's view of the agent's operations on a customer's own files. The API "spawns nothing at
/// all" and never touches a customer's disk (rules/security.md item 3), so a file inside a home is
/// read, listed or written by the agent under that account's uid or it is not.
/// </summary>
/// <remarks>
/// Introduced by the TLS work, whose ACME HTTP-01 challenge token lives at
/// <c>&lt;document root&gt;/.well-known/acme-challenge/&lt;token&gt;</c> — inside the customer's home,
/// therefore a customer file. The file manager added the read side (list, search, read) and then the
/// operations that change a customer's tree (create, move, copy, permissions, trash, archives), each
/// with a caller in the Files module (YAGNI, rules/architecture.md), so an agent rpc the panel does
/// not call has no method here.
/// </remarks>
public interface IAgentFilesClient
{
    /// <summary>Writes one file under an account's home, as that account.</summary>
    /// <param name="accountUsername">System username of the owning account, whose uid the write runs under.</param>
    /// <param name="path">Destination path, relative to the account home; the agent canonicalizes and contains it.</param>
    /// <param name="content">The file's entire content. Small payloads only — this sends a single chunk.</param>
    /// <param name="mode">Permission bits for the created or overwritten file, e.g. <c>0644</c> as octal.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>How many bytes the agent wrote, or a typed failure.</returns>
    Task<Result<ulong>> WriteFileAsync(
        string accountUsername,
        string path,
        string content,
        uint mode,
        CancellationToken cancellationToken);

    /// <summary>Writes one file under an account's home, as that account, from a stream.</summary>
    /// <param name="accountUsername">System username of the owning account, whose uid the write runs under.</param>
    /// <param name="path">Destination path, relative to the account home; the agent canonicalizes and contains it.</param>
    /// <param name="mode">Permission bits for the created or overwritten file, e.g. <c>0644</c> as octal.</param>
    /// <param name="body">The content, read once from its current position to its end.</param>
    /// <param name="maxBytes">The most body bytes the agent accepts; above its own ceiling the agent refuses the call.</param>
    /// <param name="cancellationToken">Cancellation for the whole upload.</param>
    /// <returns>
    /// How many bytes the agent wrote, or a typed failure — <c>AgentQuotaExceeded</c> when the account's
    /// quota ends the write, a validation failure when the body is larger than <paramref name="maxBytes"/>,
    /// <c>AgentLimitReached</c> when <paramref name="maxBytes"/> is above the agent's ceiling.
    /// </returns>
    /// <remarks>
    /// An existing regular file at the path is replaced. The body is consumed, so the call is never
    /// replayed by a retry policy.
    /// </remarks>
    Task<Result<ulong>> WriteFileStreamAsync(
        string accountUsername,
        string path,
        uint mode,
        Stream body,
        ulong maxBytes,
        CancellationToken cancellationToken);

    /// <summary>Deletes one file or directory under an account's home, as that account.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="path">Path to delete, relative to the account home.</param>
    /// <param name="recursive">Whether a non-empty directory may be removed with its contents.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — including <c>AgentNotFound</c> for a path already gone.</returns>
    Task<Result> DeleteEntryAsync(
        string accountUsername,
        string path,
        bool recursive,
        CancellationToken cancellationToken);

    /// <summary>Lists one page of a directory under an account's home, as that account.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="path">Directory path relative to the account home; empty is the home itself.</param>
    /// <param name="offset">Entries to skip, in the chosen order, after the hidden filter.</param>
    /// <param name="limit">Most entries in the page; zero is the agent's default, and the agent refuses above 1000.</param>
    /// <param name="includeHidden">Whether entries whose name starts with "." are listed.</param>
    /// <param name="sort">The order within directories and within other entries; directories come first.</param>
    /// <param name="descending">Whether the order chosen by <paramref name="sort"/> is reversed.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The page and the directory's total, or a typed failure — <c>AgentTooLarge</c> for a directory
    /// holding more names than a listing can describe, <c>AgentValidationFailed</c> for a path that
    /// is a link, not a directory the account owns, or missing, and <c>AgentSaturated</c> when the
    /// agent is running as many browse operations as it admits.
    /// </returns>
    Task<Result<DirectoryListingDto>> ListDirectoryAsync(
        string accountUsername,
        string path,
        uint offset,
        uint limit,
        bool includeHidden,
        DirectorySortOrder sort,
        bool descending,
        CancellationToken cancellationToken);

    /// <summary>Searches an account's tree for entries whose name contains a substring, as that account.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="path">Directory to search from, relative to the account home; empty is the home itself.</param>
    /// <param name="query">The substring of an entry name, matched case-insensitively; not a pattern.</param>
    /// <param name="maxDepth">Deepest level searched; zero is the agent's default and the agent clamps above 16.</param>
    /// <param name="maxResults">Most matches returned; zero is the agent's default and the agent clamps above 500.</param>
    /// <param name="includeHidden">Whether hidden entries are matched and hidden directories descended into.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// The matches, with <see cref="FileSearchResultDto.Truncated"/> set when a bound stopped the
    /// search — a success, never a failure — or a typed failure for a path that cannot be searched.
    /// </returns>
    Task<Result<FileSearchResultDto>> SearchEntriesAsync(
        string accountUsername,
        string path,
        string query,
        uint maxDepth,
        uint maxResults,
        bool includeHidden,
        CancellationToken cancellationToken);

    /// <summary>Opens one file under an account's home for reading, as that account.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="path">File path relative to the account home.</param>
    /// <param name="maxBytes">
    /// The most bytes the agent will stream: a larger file is refused before the first byte, and one
    /// that grows past it while read ends the stream in an error. Required; the agent refuses zero and
    /// anything above its own ceiling of 512 MiB.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation for the call. It governs the whole stream and not only its start, so it must stay
    /// alive until the returned stream has been read and disposed.
    /// </param>
    /// <returns>
    /// The opened stream, which the caller must dispose; or a typed failure — <c>AgentTooLarge</c>
    /// for a file over <paramref name="maxBytes"/>, <c>AgentValidationFailed</c> for an entry that is
    /// not a regular file the account owns with a single link, <c>AgentNotFound</c> for a missing one.
    /// </returns>
    /// <remarks>
    /// Not retried by the resilience layer, and not bounded by its timeout: a download is not an
    /// operation with a duration the pipeline can judge, and its timeout token would not outlive the
    /// open. The transport failing is answered here as a typed failure, as it is for a stream of log
    /// lines.
    /// </remarks>
    Task<Result<OpenedFileRead>> ReadFileAsync(
        string accountUsername,
        string path,
        ulong maxBytes,
        CancellationToken cancellationToken);

    /// <summary>Creates a directory, with any missing parents, under an account's home, as that account.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="path">Directory path relative to the account home.</param>
    /// <param name="mode">Plain permission bits of the final directory; the agent refuses setuid, setgid and sticky.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — <c>AgentAlreadyExists</c> when anything holds the final name.</returns>
    Task<Result> CreateDirectoryAsync(
        string accountUsername,
        string path,
        uint mode,
        CancellationToken cancellationToken);

    /// <summary>Creates an empty file under an account's home, as that account, refusing to replace anything.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="path">File path relative to the account home.</param>
    /// <param name="mode">Plain permission bits of the new file.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — <c>AgentAlreadyExists</c> when anything holds the name.</returns>
    /// <remarks>
    /// The agent has no create-file rpc: this is <c>WriteFile</c> with an empty body and
    /// <c>fail_if_exists</c> set, which is the one write that can never replace what is there.
    /// </remarks>
    Task<Result> CreateFileAsync(
        string accountUsername,
        string path,
        uint mode,
        CancellationToken cancellationToken);

    /// <summary>Moves or renames an entry within an account's home, by one rename that replaces nothing.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="sourcePath">The entry to move, relative to the account home.</param>
    /// <param name="destinationPath">Where it goes, relative to the same home; must not exist.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — <c>AgentAlreadyExists</c> for a taken destination.</returns>
    Task<Result> MoveEntryAsync(
        string accountUsername,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken);

    /// <summary>Copies a file or a directory tree within an account's home, all or nothing.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="sourcePath">The entry to copy, relative to the account home.</param>
    /// <param name="destinationPath">Where the copy goes, relative to the same home; must not exist.</param>
    /// <param name="maxEntries">The most entries the copy may hold; required, the agent refuses zero.</param>
    /// <param name="maxBytes">The most bytes of file content the copy may hold; required, the agent refuses zero.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — <c>AgentLimitReached</c> when a bound ends the copy.</returns>
    Task<Result> CopyEntryAsync(
        string accountUsername,
        string sourcePath,
        string destinationPath,
        ulong maxEntries,
        ulong maxBytes,
        CancellationToken cancellationToken);

    /// <summary>Changes the permission bits of an entry, optionally through a whole tree.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="path">The entry to change, relative to the account home.</param>
    /// <param name="mode">Plain permission bits for the entry, and for files of a recursive change.</param>
    /// <param name="recursive">Whether a directory's contents are changed too.</param>
    /// <param name="directoryMode">Bits for the directories of a recursive change; zero means <paramref name="mode"/>.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>How many entries were changed and how many were left alone, or a typed failure.</returns>
    Task<Result<PermissionChangeDto>> ChangePermissionsAsync(
        string accountUsername,
        string path,
        uint mode,
        bool recursive,
        uint directoryMode,
        CancellationToken cancellationToken);

    /// <summary>Moves an entry into the account's trash.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="path">The entry to trash, relative to the account home.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The identifier the entry now has in the trash, or a typed failure.</returns>
    Task<Result<string>> TrashEntryAsync(
        string accountUsername,
        string path,
        CancellationToken cancellationToken);

    /// <summary>Lists what is in the account's trash, newest first.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The entries the agent reports, or a typed failure.</returns>
    Task<Result<IReadOnlyList<TrashedEntryDto>>> ListTrashAsync(
        string accountUsername,
        CancellationToken cancellationToken);

    /// <summary>Moves one trash entry back into the home without replacing anything.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="trashId">The entry's identifier, as <see cref="ListTrashAsync"/> reports it.</param>
    /// <param name="destinationPath">Where to put it; empty means the path it had when it was trashed.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The path the entry now has, or a typed failure — <c>AgentAlreadyExists</c> for a taken place.</returns>
    Task<Result<string>> RestoreTrashEntryAsync(
        string accountUsername,
        string trashId,
        string destinationPath,
        CancellationToken cancellationToken);

    /// <summary>Permanently removes everything in the account's trash.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>How many trash entries were removed, or a typed failure.</returns>
    Task<Result<ulong>> EmptyTrashAsync(string accountUsername, CancellationToken cancellationToken);

    /// <summary>Permanently removes the trash entries older than a number of days.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="olderThanDays">Entries trashed more than this many days ago go; the agent refuses zero.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>How many trash entries were removed, or a typed failure.</returns>
    Task<Result<ulong>> PurgeExpiredTrashAsync(
        string accountUsername,
        uint olderThanDays,
        CancellationToken cancellationToken);

    /// <summary>Removes a file, a link or a whole directory tree, as the account, without using the trash.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="path">The entry to remove, relative to the account home.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or a typed failure — <c>AgentNotFound</c> for a path already gone.</returns>
    Task<Result> ForceDeleteEntryAsync(
        string accountUsername,
        string path,
        CancellationToken cancellationToken);

    /// <summary>Creates an archive of one or more paths under an account's home.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="sourcePaths">The paths to include, each relative to the account home.</param>
    /// <param name="destinationPath">The archive's path, relative to the account home.</param>
    /// <param name="format">The container format to write.</param>
    /// <param name="maxEntries">The most entries the sources may hold together; required.</param>
    /// <param name="maxSourceBytes">The most source bytes the archive may be made from; required.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The archive's size in bytes, or a typed failure — <c>AgentLimitReached</c> when a bound ends it.</returns>
    Task<Result<ulong>> CreateArchiveAsync(
        string accountUsername,
        IReadOnlyList<string> sourcePaths,
        string destinationPath,
        AgentArchiveFormat format,
        ulong maxEntries,
        ulong maxSourceBytes,
        CancellationToken cancellationToken);

    /// <summary>Extracts an archive under an account's home, refusing any member that would land outside the destination.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="archivePath">The archive, relative to the account home.</param>
    /// <param name="destinationPath">The directory to extract into, relative to the account home; created if absent.</param>
    /// <param name="format">The archive's container format; it must match the file.</param>
    /// <param name="maxEntries">The most members the archive may hold; required.</param>
    /// <param name="maxExpandedBytes">The most bytes the extraction may write; required.</param>
    /// <param name="maxRatio">The largest accepted ratio of expanded bytes to the archive's size; required.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>How many entries were extracted, or a typed failure — <c>AgentArchiveUnsafe</c> for an unsafe member.</returns>
    Task<Result<uint>> ExtractArchiveAsync(
        string accountUsername,
        string archivePath,
        string destinationPath,
        AgentArchiveFormat format,
        ulong maxEntries,
        ulong maxExpandedBytes,
        uint maxRatio,
        CancellationToken cancellationToken);
}
