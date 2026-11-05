using Maran.Modules.Sftp.Common;
using Maran.Modules.Sftp.Mappers;
using Maran.Modules.Sftp.Persistence;
using Maran.Modules.Sftp.Resources;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Sftp.Queries.ListSftpUserKeys;

/// <summary>
/// Handles <see cref="ListSftpUserKeysQuery"/> by reading one page of <c>sftp.SftpUserKeys</c> for a
/// login the caller may see.
/// </summary>
/// <remarks>
/// The login is looked up first through the tenant-filtered context, so a login of another account
/// answers 404 — never an empty page, which would confirm nothing but also never 403. The keys are
/// then read through the same filter, which reaches the owning account through the login. The key
/// body is never selected into the answer; the agent is never consulted, and no handler here may be
/// handed one.
/// </remarks>
public sealed class ListSftpUserKeysQueryHandler
{
    /// <summary>The Sftp module's database context, and this module's tenant boundary.</summary>
    private readonly SftpDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Sftp module's database context.</param>
    public ListSftpUserKeysQueryHandler(SftpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Answers the requested page of the login's keys with the total that matched.</summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The page, or <c>SftpUserNotFound</c> when the caller may not see the login.</returns>
    public async Task<Result<PagedResult<SftpUserKeyDto>>> HandleAsync(
        ListSftpUserKeysQuery query,
        CancellationToken cancellationToken)
    {
        var exists = await _dbContext.SftpUsers
            .AsNoTracking()
            .AnyAsync(row => row.Id == query.SftpUserId, cancellationToken);
        if (!exists)
        {
            return Result<PagedResult<SftpUserKeyDto>>.Fail(
                Error.Of(nameof(ErrorMessages.SftpUserNotFound), ErrorType.NotFound));
        }

        var keys = _dbContext.SftpUserKeys
            .AsNoTracking()
            .Where(key => key.SftpUserId == query.SftpUserId);

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the wildcards
            // so the term matches literally.
            var pattern = LikePattern.Contains(query.Search.Value);
            keys = keys.Where(key => EF.Functions.ILike(key.Label, pattern, LikePattern.EscapeCharacter));
        }

        var page = await keys.ToPagedAsync(query, ListSftpUserKeysSorts.Map, cancellationToken);

        return Result<PagedResult<SftpUserKeyDto>>.Ok(page.Map(row =>
        {
            return SftpUserKeyMapper.From(row);
        }));
    }
}
