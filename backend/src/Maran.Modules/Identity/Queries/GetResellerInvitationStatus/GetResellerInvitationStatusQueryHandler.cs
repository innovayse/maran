using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;

namespace Maran.Modules.Identity.Queries.GetResellerInvitationStatus;

/// <summary>
/// Handles <see cref="GetResellerInvitationStatusQuery"/>: reads, fresh from the stored login and
/// token rows, whether there is still an invitation an administrator could hand over to a reseller.
/// </summary>
/// <remarks>
/// Which login to ask about is this handler's part (the one carrying the reseller's id); reading the
/// state, with the guarantees that a read mints nothing and offers no dead link, is
/// <see cref="InvitationStatusReader"/>'s. A reseller with no login reads as <c>none</c>.
/// </remarks>
public sealed class GetResellerInvitationStatusQueryHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Reads where a login stands in its invitation.</summary>
    private readonly InvitationStatusReader _reader;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="reader">Reads where a login stands in its invitation.</param>
    public GetResellerInvitationStatusQueryHandler(IdentityDbContext dbContext, InvitationStatusReader reader)
    {
        _dbContext = dbContext;
        _reader = reader;
    }

    /// <summary>Reports the state of the reseller's invitation.</summary>
    /// <param name="query">The reseller being asked about.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Always a success: no login is the <c>none</c> answer, not a failure.</returns>
    public async Task<Result<InvitationStatusDto>> HandleAsync(
        GetResellerInvitationStatusQuery query,
        CancellationToken cancellationToken)
    {
        var login = await _dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.ResellerId == query.ResellerId,
            cancellationToken);

        return Result<InvitationStatusDto>.Ok(await _reader.ReadAsync(login, cancellationToken));
    }
}
