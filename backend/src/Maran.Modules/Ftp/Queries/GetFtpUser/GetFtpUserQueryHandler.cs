using Maran.Modules.Ftp.Common;
using Maran.Modules.Ftp.Domain.Policies;
using Maran.Modules.Ftp.Persistence;
using Maran.Modules.Ftp.Resources;

namespace Maran.Modules.Ftp.Queries.GetFtpUser;

/// <summary>Handles <see cref="GetFtpUserQuery"/> by reading one row within the caller's tenant scope.</summary>
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
public sealed class GetFtpUserQueryHandler
{
    /// <summary>The Ftp module's database context, and this module's tenant boundary.</summary>
    private readonly FtpDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Ftp module's database context.</param>
    public GetFtpUserQueryHandler(FtpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Returns the login, or <c>FtpUserNotFound</c>.</summary>
    /// <param name="query">Which login to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The login's view, or <c>FtpUserNotFound</c>.</returns>
    public async Task<Result<FtpUserDto>> HandleAsync(GetFtpUserQuery query, CancellationToken cancellationToken)
    {
        var ftpUser = await _dbContext.FtpUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == query.FtpUserId, cancellationToken);

        if (ftpUser is null)
        {
            return Result<FtpUserDto>.Fail(Error.Of(nameof(ErrorMessages.FtpUserNotFound), ErrorType.NotFound));
        }

        return Result<FtpUserDto>.Ok(new FtpUserDto(
            ftpUser.Id,
            ftpUser.AccountId,
            ftpUser.Name,
            ftpUser.FullName,
            FtpsProtocolName.Ftps,
            ftpUser.CreatedAt));
    }
}
