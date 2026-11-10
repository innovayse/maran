using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Npgsql;

namespace Maran.Modules.Identity.IntegrationEvents.Handlers;

/// <summary>
/// Moves a hosting account's login to the owner address an administrator just set
/// (<see cref="AccountLoginEmailChangeRequested"/>), so its invitation and its password resets are
/// mailed there from now on.
/// </summary>
/// <remarks>
/// <para>
/// <b>An address another login already holds is refused, not shared</b> (<c>EmailTaken</c>). A
/// password reset is asked for BY ADDRESS, so two logins on one address would make "which account
/// does this reset mail unlock" a question with two answers. <c>IX_Users_Email</c> is the backstop:
/// a second change claiming the same address in the same instant fails its save with
/// <c>SqlState 23505</c> and nothing else, and is answered the same way.
/// </para>
/// <para>
/// <b>No login is a success</b>, for the reason the request's own remarks give. Neither the state of
/// the login (invited or active) nor the account's suspension matters: every one of them is mailed at
/// this address. The login is the account's OWNER login, found through the owner membership.
/// </para>
/// <para>
/// <b>The invitation and reset tokens mailed to the address being left are retired in the same
/// save.</b> The old address may be a typo, a former contact or a mailbox somebody else reads, and a
/// token mailed there would otherwise still set the owner login's password for its whole life (seven
/// days for an invitation). A new token is issued for the new address afterwards and is not touched.
/// </para>
/// <para>
/// <b>The change itself is journaled by the Accounts module</b>, with the administrator who asked and
/// from where; this handler writes only the entry for the tokens it retired.
/// </para>
/// </remarks>
public sealed class AccountLoginEmailChangeRequestedHandler
{
    /// <summary>The Identity module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Finds the account's owner login.</summary>
    private readonly AccountOwnerFinder _owners;

    /// <summary>Retires the invitation and reset tokens mailed to the address being left.</summary>
    private readonly CredentialTokenRetirement _tokenRetirement;

    /// <summary>Records the retirement of those tokens.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Identity module's database context.</param>
    /// <param name="owners">Finds the account's owner login.</param>
    /// <param name="tokenRetirement">Retires the tokens mailed to the old address.</param>
    /// <param name="journal">Records the retirement.</param>
    /// <param name="clock">The panel's clock.</param>
    public AccountLoginEmailChangeRequestedHandler(
        IdentityDbContext dbContext,
        AccountOwnerFinder owners,
        CredentialTokenRetirement tokenRetirement,
        IdentityAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _owners = owners;
        _tokenRetirement = tokenRetirement;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Moves the account's login to the new address, or refuses.</summary>
    /// <param name="message">The account and its new owner address.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Success, or <c>EmailTaken</c> when another login already holds the address.</returns>
    public async Task<Result> HandleAsync(
        AccountLoginEmailChangeRequested message,
        CancellationToken cancellationToken)
    {
        var owner = await _owners.FindAsync(message.AccountId, cancellationToken);
        if (owner is null)
        {
            return Result.Ok();
        }

        var user = owner.Login;

        var taken = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(other => other.Email == message.Email && other.Id != user.Id, cancellationToken);
        if (taken)
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.EmailTaken), ErrorType.Conflict));
        }

        // Only a real move retires anything: keeping the address leaves its mail where it was sent.
        var moved = !string.Equals(user.Email, message.Email, StringComparison.Ordinal);
        var retired = 0;
        if (moved)
        {
            retired = await _tokenRetirement.RetireAsync(user.Id, _clock.UtcNow, cancellationToken);
        }

        user.ChangeEmail(message.Email);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The ONE narrowing: only 23505 means another login claimed the address between the
            // check above and this save. The change is undone so the context is not left dirty.
            _dbContext.Entry(user).State = EntityState.Detached;
            return Result.Fail(Error.Of(nameof(ErrorMessages.EmailTaken), ErrorType.Conflict));
        }

        if (retired > 0)
        {
            await _journal.RecordSystemAsync(
                IdentityAuditActions.CredentialTokensRetired, $"{user.Id}: {retired}", succeeded: true, cancellationToken);
        }

        return Result.Ok();
    }
}
