using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Npgsql;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// The one place an administrator-driven invitation token is minted for a reseller's login: finds
/// the login, refuses what must not be invited, retires every outstanding token and issues a new one.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reseller's counterpart of <see cref="InvitationIssuer"/>, sharing its minting.</b> Resending
/// the invitation mails it and creating a link hands it to the administrator; they differ only in
/// where the plaintext goes, so the refusals live here once. The token itself is minted by
/// <see cref="InvitationIssuer.MintAsync"/>, which is why a reseller's token is spent, stored and
/// accepted exactly as an account owner's is.
/// </para>
/// <para>
/// <b>The login is found by the reseller id it carries</b> (<c>UX_Users_ResellerId</c>), not through a
/// membership: a reseller's login holds no membership of any account. A reseller whose login was
/// never created (its <c>ResellerCreated</c> subscriber failed while the reseller row stood) is
/// <b>rebuilt here</b>, exactly as <c>ResellerCreatedHandler</c> would have built it, from the reseller's
/// own directory entry (name and contact address) — the counterpart of the account side rebuilding a
/// missing owner login. The caller's invitation journal entry records the rebuild with the invitation
/// it issues. A suspended reseller gets no login created.
/// </para>
/// <para>
/// <b>The rebuild races, and the loser joins the winner.</b> Two administrators asking at once can both
/// read "no login"; <c>UX_Users_ResellerId</c> stops the second row. After the unique violation the login
/// is read again: if it exists the winner built it and this call mints its token over it (retiring the
/// winner's, so one live invitation remains); if it does not, the name or the address belongs to another
/// login, a real failure, and the exception is rethrown rather than reported as an invitation.
/// </para>
/// <para>
/// <b>A login that is not <see cref="UserLifecycle.Invited"/> is refused</b>, for the reason the
/// account counterpart gives: a fresh token for a login that already has a password is a password
/// reset in disguise, and accepting it would overwrite the password. <b>A suspended reseller is
/// refused too</b> (<c>InvitationResellerSuspended</c>): handing a suspended reseller a fresh
/// credential is an offer the suspension exists to withdraw.
/// </para>
/// </remarks>
public sealed class ResellerInvitationIssuer
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The read-only window onto the Accounts module: the reseller's state and language.</summary>
    private readonly IResellerDirectory _resellerDirectory;

    /// <summary>Retires the outstanding tokens and mints the new one.</summary>
    private readonly InvitationIssuer _issuer;

    /// <summary>Creates the service.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="resellerDirectory">The read-only window onto the Accounts module.</param>
    /// <param name="issuer">Retires the outstanding tokens and mints the new one.</param>
    public ResellerInvitationIssuer(
        IdentityDbContext dbContext,
        IResellerDirectory resellerDirectory,
        InvitationIssuer issuer)
    {
        _dbContext = dbContext;
        _resellerDirectory = resellerDirectory;
        _issuer = issuer;
    }

    /// <summary>Retires every outstanding token of the reseller's login, issues a new one and saves.</summary>
    /// <param name="resellerId">The reseller whose login is being (re)invited.</param>
    /// <param name="now">The current instant, taken from <see cref="IClock"/> by the caller.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The invitation with its plaintext token, or a typed refusal: no such reseller, a reseller with
    /// no login, a login that already has a password, or a suspended reseller.
    /// </returns>
    public async Task<Result<IssuedInvitation>> IssueAsync(
        Guid resellerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var reseller = await _resellerDirectory.FindAsync(resellerId, cancellationToken);
        if (reseller is null)
        {
            return Result<IssuedInvitation>.Fail(
                Error.Of(nameof(ErrorMessages.InvitationResellerNotFound), ErrorType.NotFound));
        }

        var login = await FindLoginAsync(resellerId, cancellationToken);
        if (login is null)
        {
            if (reseller.State == ResellerState.Suspended)
            {
                // A suspended reseller is offered no way in, and a login created here would be one.
                return Result<IssuedInvitation>.Fail(
                    Error.Of(nameof(ErrorMessages.InvitationResellerSuspended), ErrorType.Conflict));
            }

            login = await RebuildLoginAsync(reseller, now, cancellationToken);
        }

        if (login.Lifecycle != UserLifecycle.Invited)
        {
            return Result<IssuedInvitation>.Fail(
                Error.Of(nameof(ErrorMessages.InvitationLoginNotPending), ErrorType.Conflict));
        }

        if (reseller.State == ResellerState.Suspended)
        {
            return Result<IssuedInvitation>.Fail(
                Error.Of(nameof(ErrorMessages.InvitationResellerSuspended), ErrorType.Conflict));
        }

        var minted = await _issuer.MintAsync(login, now, cancellationToken);

        return Result<IssuedInvitation>.Ok(
            new IssuedInvitation(login.Username, login.Email, minted.Token, minted.ExpiresAt, reseller.Language));
    }

    /// <summary>Finds the reseller's login by the reseller id it carries.</summary>
    /// <param name="resellerId">The reseller.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The login, or <c>null</c> when none was ever created.</returns>
    private async Task<User?> FindLoginAsync(Guid resellerId, CancellationToken cancellationToken)
    {
        return await _dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.ResellerId == resellerId,
            cancellationToken);
    }

    /// <summary>
    /// Creates the reseller's missing login exactly as <c>ResellerCreatedHandler</c> would have, from
    /// the reseller's directory entry, and saves it.
    /// </summary>
    /// <param name="reseller">The reseller whose login is missing, as its directory entry reads.</param>
    /// <param name="now">The current instant, taken from <see cref="IClock"/>.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The login this call inserted, or the one a concurrent call won the race to insert.</returns>
    /// <exception cref="DbUpdateException">
    /// Thrown when the insert violated a unique index and no login for the reseller exists afterwards:
    /// the name or the address is held by another login, which is a real failure and not a race.
    /// </exception>
    private async Task<User> RebuildLoginAsync(
        ResellerSnapshot reseller,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var login = User.InviteReseller(Guid.NewGuid(), reseller.Name, reseller.ContactEmail, reseller.Id, now);
        _dbContext.Users.Add(login);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Nothing this call added exists (SaveChanges rolled the insert back); the entity is
            // detached rather than left tracked as a pending add on a context the caller keeps using.
            _dbContext.Entry(login).State = EntityState.Detached;

            var existing = await FindLoginAsync(reseller.Id, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return existing;
        }

        return login;
    }
}
