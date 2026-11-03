using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Events;

namespace Maran.Modules.Identity.IntegrationEvents.Handlers;

/// <summary>
/// Lets the login of a reactivated reseller sign in again (<see cref="ResellerResuming"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It does not restore sessions, and that is the law rather than an omission.</b>
/// <see cref="ResellerSuspendingHandler"/> revoked them, and a revoked session is gone the same way a
/// spent one is: the reseller signs in again and is issued a fresh one.
/// </para>
/// <para>
/// <b>It restores only what the suspension changed</b>: the login's own suspension. A login that was
/// still waiting for its invitation to be accepted stays waiting, because the suspension never
/// touched that fact. No hosting account is touched either way.
/// </para>
/// <para>
/// <b>The failure is not swallowed.</b> Anything thrown here propagates to the Accounts handler,
/// which abandons the reactivation with the reseller left suspended — the recoverable direction.
/// A reseller with no login has nothing to resume.
/// </para>
/// <para>
/// <b>It writes no audit entry of its own</b>, for the reason <see cref="AccountDeletingHandler"/> gives.
/// </para>
/// </remarks>
public sealed class ResellerResumingHandler
{
    /// <summary>The Identity module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Identity module's database context.</param>
    public ResellerResumingHandler(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Lifts the suspension of the reseller's login.</summary>
    /// <param name="message">The reseller about to be reactivated.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Resolves once the login may be used again.</returns>
    public async Task HandleAsync(ResellerResuming message, CancellationToken cancellationToken)
    {
        var login = await _dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.ResellerId == message.ResellerId, cancellationToken);
        if (login is null)
        {
            return;
        }

        login.Resume();
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
