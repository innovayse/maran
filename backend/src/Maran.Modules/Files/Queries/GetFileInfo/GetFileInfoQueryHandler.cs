using Maran.Modules.Files.Common;
using Maran.Modules.Files.Mappers;
using Maran.Modules.Files.Models;
using Maran.Modules.Files.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Files.Queries.GetFileInfo;

/// <summary>Handles <see cref="GetFileInfoQuery"/>: asks the agent's folder listing about one entry.</summary>
/// <remarks>
/// The owner shown is read-only information the agent reports; nothing here lets a caller change it.
/// An entry that is not there answers <c>FilesEntryNotFound</c> — 404, never 403, and the same answer
/// another tenant's path gets, because the account is admitted before the agent is asked.
/// </remarks>
public sealed class GetFileInfoQueryHandler
{
    /// <summary>Admits the account: tenant scope, then the suspended-account rule.</summary>
    private readonly FilesAccountGate _gate;

    /// <summary>Finds the entry at the path.</summary>
    private readonly FilesEntryLookup _lookup;

    /// <summary>Names an entry's kind in the request's culture.</summary>
    private readonly FilesDisplayNames _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="gate">Admits the account.</param>
    /// <param name="lookup">Finds the entry at the path.</param>
    /// <param name="names">Names an entry's kind in the request's culture.</param>
    public GetFileInfoQueryHandler(FilesAccountGate gate, FilesEntryLookup lookup, FilesDisplayNames names)
    {
        _gate = gate;
        _lookup = lookup;
        _names = names;
    }

    /// <summary>Reads the entry's properties.</summary>
    /// <param name="query">The validated query; see <see cref="GetFileInfoQueryValidator"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The properties — or <c>AccountNotFound</c>, <c>FilesAccountSuspended</c>, <c>FilesEntryNotFound</c>, or the
    /// module's translation of another agent refusal.
    /// </returns>
    public async Task<Result<FileInfoDto>> HandleAsync(GetFileInfoQuery query, CancellationToken cancellationToken)
    {
        var admitted = await _gate.AdmitAsync(
            query.AccountId,
            FileAuditActions.FileAccessRefused,
            new FilesCallOrigin(query.IpAddress, query.UserAgent),
            cancellationToken);
        if (!admitted.IsSuccess)
        {
            return Result<FileInfoDto>.Fail(admitted.Error!);
        }

        var found = await _lookup.FindAsync(admitted.Value.Username, query.AccountId, query.Path, cancellationToken);
        if (!found.IsSuccess)
        {
            return Result<FileInfoDto>.Fail(found.Error!);
        }

        var entry = found.Value;
        return Result<FileInfoDto>.Ok(
            FileInfoMapper.From(entry, query.Path, _names.KindOf(FileEntryMapper.KindOf(entry.Kind))));
    }
}
