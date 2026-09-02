using Maran.Modules.Sftp.Common;
using Maran.Modules.Sftp.Persistence;
using Maran.Modules.Sftp.Resources;

namespace Maran.Modules.Sftp.Queries.GetSftpUser;

/// <summary>Handles <see cref="GetSftpUserQuery"/> by reading one row within the caller's tenant scope.</summary>
/// <remarks>
/// Another tenant's login is not found rather than forbidden, and that is not a politeness: 403
/// confirms the id names a real login, which turns this endpoint into an oracle for enumerating
/// other customers' access (rules/testing.md item 3). The distinction is not made by this handler at
/// all — the context's query filter means the row genuinely is not there.
///
/// It exists beside the listing because creating a login answers with a <c>Location</c> pointing
/// here, and a created-resource header that resolves to nothing is a lie the client cannot follow.
///
/// The answer carries no password, and there is nothing here for it to carry one from: no column
/// holds one. The value was shown once, when the login was created or its password last reset.
/// </remarks>
public sealed class GetSftpUserQueryHandler
{
    /// <summary>The Sftp module's database context, and this module's tenant boundary.</summary>
    private readonly SftpDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Sftp module's database context.</param>
    public GetSftpUserQueryHandler(SftpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Returns the login, or <c>SftpUserNotFound</c>.</summary>
    /// <param name="query">Which login to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The login's view, or <c>SftpUserNotFound</c>.</returns>
    public async Task<Result<SftpUserDto>> HandleAsync(GetSftpUserQuery query, CancellationToken cancellationToken)
    {
        var sftpUser = await _dbContext.SftpUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == query.SftpUserId, cancellationToken);

        if (sftpUser is null)
        {
            return Result<SftpUserDto>.Fail(Error.Of(nameof(ErrorMessages.SftpUserNotFound)));
        }

        return Result<SftpUserDto>.Ok(new SftpUserDto(
            sftpUser.Id,
            sftpUser.AccountId,
            sftpUser.Name,
            sftpUser.FullName,
            sftpUser.CreatedAt));
    }
}
