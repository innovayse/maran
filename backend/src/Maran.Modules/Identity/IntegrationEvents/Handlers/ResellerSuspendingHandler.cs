using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Events;

namespace Maran.Modules.Identity.IntegrationEvents.Handlers;

/// <summary>
/// Suspends the login of a reseller that is about to be suspended, and ends the sessions that login
/// holds (<see cref="ResellerSuspending"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It acts on the reseller's LOGIN and on nothing else.</b> A reseller's suspension touches no
/// hosting account: the accounts it owns are its customers' services and keep running, and each
/// customer login keeps its memberships. A reseller's login holds no membership, so the suspension
/// is stored on the login itself (<see cref="Domain.Entities.User.IsSuspended"/>), where every path
/// that mints a token already asks (<see cref="Domain.Entities.User.MayAuthenticate"/>).
/// </para>
/// <para>
/// <b>Closing the door is not enough on its own.</b> Only the NEXT sign-in or refresh reads the
/// suspension; an access token issued before it keeps authenticating for the rest of its own
/// lifetime unless the sessions behind it are revoked here. Every live session of the login ends,
/// including a view-as session an administrator opened on it (<see cref="SessionRevocationReason.ResellerSuspended"/>).
/// The Accounts module's mutating handlers additionally refuse a suspended reseller, which covers
/// the fifteen minutes of an already-issued token.
/// </para>
/// <para>
/// <b>Repeating it is harmless.</b> A login already suspended stays so, and a reseller with no login
/// (its issuance failed and has not been repeated) has nothing to do.
/// </para>
/// <para>
/// <b>The failure is not swallowed.</b> Anything thrown here propagates to the Accounts handler,
/// which abandons the suspension with the reseller left active — the recoverable direction, for the
/// reason <see cref="ResellerSuspending"/> gives.
/// </para>
/// <para>
/// <b>It writes no audit entry of its own</b>, for the reason <see cref="AccountDeletingHandler"/>
/// gives: the Accounts module journals the suspension with who asked and from where.
/// </para>
/// </remarks>
public sealed class ResellerSuspendingHandler
{
    /// <summary>The Identity module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Ends the login's live sessions.</summary>
    private readonly ISessionService _sessionService;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Identity module's database context.</param>
    /// <param name="sessionService">Ends the login's live sessions.</param>
    public ResellerSuspendingHandler(IdentityDbContext dbContext, ISessionService sessionService)
    {
        _dbContext = dbContext;
        _sessionService = sessionService;
    }

    /// <summary>Suspends the reseller's login and revokes every session it holds.</summary>
    /// <param name="message">The reseller about to be suspended.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Resolves once the login is suspended and its sessions are gone.</returns>
    public async Task HandleAsync(ResellerSuspending message, CancellationToken cancellationToken)
    {
        var login = await _dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.ResellerId == message.ResellerId, cancellationToken);
        if (login is null)
        {
            // No login acts for this reseller: nothing to close. See the type's remarks.
            return;
        }

        login.Suspend();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _sessionService.RevokeAllAsync(login.Id, SessionRevocationReason.ResellerSuspended, cancellationToken);
    }
}
