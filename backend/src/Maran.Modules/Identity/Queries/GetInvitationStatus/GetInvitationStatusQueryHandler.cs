using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Identity.Queries.GetInvitationStatus;

/// <summary>
/// Handles <see cref="GetInvitationStatusQuery"/>: reads, fresh from the stored login and token
/// rows, whether there is still an invitation an administrator could hand over.
/// </summary>
/// <remarks>
/// <para>
/// <b>It mints nothing and returns nothing secret.</b> The answer is a state and, for an outstanding
/// invitation, the instant it lapses. The token's digest is stored and the plaintext is not, so there
/// is nothing here to return even if this were asked to — which is why a read is safe to make on
/// every visit to an account's page, and why it is a separate action from creating a link.
/// </para>
/// <para>
/// <b>Computed at the moment of the question, never remembered.</b> That is what stops the panel
/// from offering a dead link: an invitation that has been accepted reads as
/// <see cref="InvitationState.Accepted"/> and one past <c>InvitationToken.Lifetime</c> as
/// <see cref="InvitationState.Expired"/> the instant they are, with no job that has to notice and no
/// flag that can be left stale.
/// </para>
/// </remarks>
public sealed class GetInvitationStatusQueryHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Names a state in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="displayNames">Names a state in the request's culture.</param>
    public GetInvitationStatusQueryHandler(
        IdentityDbContext dbContext,
        IClock clock,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _clock = clock;
        _displayNames = displayNames;
    }

    /// <summary>Reports the state of the account owner's invitation.</summary>
    /// <param name="query">The account being asked about.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// Always a success: an account with no login is the <see cref="InvitationState.None"/> answer, not
    /// a failure, because "nothing has been issued" is exactly what the caller came to learn.
    /// </returns>
    public async Task<Result<InvitationStatusDto>> HandleAsync(
        GetInvitationStatusQuery query,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.AccountId == query.AccountId, cancellationToken);

        if (user is null)
        {
            return Result<InvitationStatusDto>.Ok(StatusOf(InvitationState.None, null));
        }

        if (user.State != UserState.Invited)
        {
            return Result<InvitationStatusDto>.Ok(StatusOf(InvitationState.Accepted, null));
        }

        var now = _clock.UtcNow;
        var unspent = await _dbContext.InvitationTokens
            .Where(token => token.UserId == user.Id && token.UsedAt == null)
            .ToListAsync(cancellationToken);

        var usable = unspent
            .Where(token =>
            {
                return token.IsUsable(now);
            })
            .OrderByDescending(token =>
            {
                return token.ExpiresAt;
            })
            .FirstOrDefault();

        return Result<InvitationStatusDto>.Ok(
            usable is null
                ? StatusOf(InvitationState.Expired, null)
                : StatusOf(InvitationState.Pending, usable.ExpiresAt));
    }

    /// <summary>Builds the answer for a state, with the state named for the request's culture.</summary>
    /// <param name="state">The state to report.</param>
    /// <param name="expiresAt">When the usable token lapses; null unless the state is pending.</param>
    /// <returns>The answer.</returns>
    private InvitationStatusDto StatusOf(InvitationState state, DateTimeOffset? expiresAt)
    {
        return new InvitationStatusDto(state, _displayNames[$"InvitationState{state}"].Value, expiresAt);
    }
}
