using Maran.Modules.Files.Common;
using Maran.Modules.Files.Domain.Policies;
using Maran.Modules.Files.Mappers;

namespace Maran.Modules.Files.Services;

/// <summary>
/// Reads back one entry a mutation has just created or renamed, so the answer carries what the file
/// manager draws as a row.
/// </summary>
/// <remarks>
/// The finding is <see cref="FilesEntryLookup"/>'s, which is where the agent's lack of an rpc describing
/// one entry is dealt with; this restates what it found as the row and names its kind in the request's
/// culture. A refusal is returned as the lookup produced it, already translated.
/// </remarks>
public sealed class FilesEntryReader
{
    /// <summary>Finds one entry by its path.</summary>
    private readonly FilesEntryLookup _lookup;

    /// <summary>Names an entry's kind in the request's culture.</summary>
    private readonly FilesDisplayNames _names;

    /// <summary>Creates the reader.</summary>
    /// <param name="lookup">Finds one entry by its path.</param>
    /// <param name="names">Names an entry's kind.</param>
    public FilesEntryReader(FilesEntryLookup lookup, FilesDisplayNames names)
    {
        _lookup = lookup;
        _names = names;
    }

    /// <summary>Finds one entry and restates it as a row.</summary>
    /// <param name="username">The account's system user name.</param>
    /// <param name="accountId">The account, named in a refusal's log line.</param>
    /// <param name="path">The entry's path relative to the home; not the home itself.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The entry as a row — or <c>FilesEntryNotFound</c>, or the translated refusal.</returns>
    public async Task<Result<FileEntryDto>> FindAsync(
        string username,
        Guid accountId,
        string path,
        CancellationToken cancellationToken)
    {
        var found = await _lookup.FindAsync(username, accountId, path, cancellationToken);
        if (!found.IsSuccess)
        {
            return Result<FileEntryDto>.Fail(found.Error!);
        }

        var entry = found.Value;
        return Result<FileEntryDto>.Ok(
            FileEntryMapper.From(
                entry,
                FilesPathSegmentsPolicy.ParentOf(path),
                _names.KindOf(FileEntryMapper.KindOf(entry.Kind))));
    }
}
