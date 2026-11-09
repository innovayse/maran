using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Reads, fresh from the stored login and token rows, where one login stands in its invitation:
/// none issued, outstanding, lapsed, or accepted. The one place that question is answered, for an
/// account owner and a reseller alike.
/// </summary>
/// <remarks>
/// <para>
/// <b>It mints nothing and returns nothing secret.</b> The answer is a state and, for an outstanding
/// invitation, the instant it lapses. The token's digest is stored and the plaintext is not, so there
/// is nothing here to return even if this were asked to.
/// </para>
/// <para>
/// <b>Computed at the moment of the question, never remembered.</b> An invitation that has been
/// accepted reads as <see cref="InvitationState.Accepted"/> and one past
/// <c>InvitationToken.Lifetime</c> as <see cref="InvitationState.Expired"/> the instant they are,
/// with no job that has to notice and no flag that can be left stale, so a dead link is never offered.
/// </para>
/// <para>
/// Which login to ask about is the caller's decision (the owner of an account, the login of a
/// reseller); this type decides nothing about who may ask.
/// </para>
/// </remarks>
public sealed class InvitationStatusReader
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Names a state in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the reader.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="displayNames">Names a state in the request's culture.</param>
    public InvitationStatusReader(IdentityDbContext dbContext, IClock clock, IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _clock = clock;
        _displayNames = displayNames;
    }

    /// <summary>Reports the state of one login's invitation.</summary>
    /// <param name="login">The login being asked about, or <c>null</c> when it has none yet.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// Always an answer: no login is the <see cref="InvitationState.None"/> state, not a failure,
    /// because "nothing has been issued" is exactly what the caller came to learn.
    /// </returns>
    public async Task<InvitationStatusDto> ReadAsync(User? login, CancellationToken cancellationToken)
    {
        if (login is null)
        {
            return new NoInvitationStatusDto(NameOf(InvitationState.None));
        }

        if (!login.HasPendingInvitation)
        {
            return new AcceptedInvitationStatusDto(NameOf(InvitationState.Accepted));
        }

        var now = _clock.UtcNow;
        var unspent = await _dbContext.InvitationTokens
            .Where(token => token.UserId == login.Id && token.UsedAt == null)
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

        return usable is null
            ? new ExpiredInvitationStatusDto(NameOf(InvitationState.Expired))
            : new PendingInvitationStatusDto(NameOf(InvitationState.Pending), usable.ExpiresAt);
    }

    /// <summary>Names a state for the request's culture.</summary>
    /// <param name="state">The state to name.</param>
    /// <returns>The state's display name.</returns>
    private string NameOf(InvitationState state)
    {
        return _displayNames[$"InvitationState{state}"].Value;
    }
}
