using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Services;

namespace Maran.Modules.Identity.Queries.GetInvitationStatus;

/// <summary>
/// Handles <see cref="GetInvitationStatusQuery"/>: reads, fresh from the stored login and token
/// rows, whether there is still an invitation an administrator could hand over.
/// </summary>
/// <remarks>
/// Which login to ask about is this handler's part (the account's owner); reading the state, with
/// the guarantees that a read mints nothing and offers no dead link, is <see cref="InvitationStatusReader"/>'s.
/// </remarks>
public sealed class GetInvitationStatusQueryHandler
{
    /// <summary>Finds the account's owner login through the owner membership.</summary>
    private readonly AccountOwnerFinder _owners;

    /// <summary>Reads where a login stands in its invitation.</summary>
    private readonly InvitationStatusReader _reader;

    /// <summary>Creates the handler.</summary>
    /// <param name="owners">Finds the account's owner login through the owner membership.</param>
    /// <param name="reader">Reads where a login stands in its invitation.</param>
    public GetInvitationStatusQueryHandler(AccountOwnerFinder owners, InvitationStatusReader reader)
    {
        _owners = owners;
        _reader = reader;
    }

    /// <summary>Reports the state of the account owner's invitation.</summary>
    /// <param name="query">The account being asked about.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// Always a success: an account with no login is the <c>none</c> answer, not a failure, because
    /// "nothing has been issued" is exactly what the caller came to learn.
    /// </returns>
    public async Task<Result<InvitationStatusDto>> HandleAsync(
        GetInvitationStatusQuery query,
        CancellationToken cancellationToken)
    {
        var owner = await _owners.FindAsync(query.AccountId, cancellationToken);

        return Result<InvitationStatusDto>.Ok(await _reader.ReadAsync(owner?.Login, cancellationToken));
    }
}
