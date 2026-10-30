using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
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
/// <b>No login is a success</b>, for the reason the request's own remarks give. The state of the
/// login (invited, active, suspended) does not matter: every one of them is mailed at this address.
/// </para>
/// <para>
/// <b>It writes no audit entry of its own.</b> The Accounts module journals the change, with the
/// administrator who asked and from where; a second entry here would record the same act twice.
/// </para>
/// </remarks>
public sealed class AccountLoginEmailChangeRequestedHandler
{
    /// <summary>The Identity module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Identity module's database context.</param>
    public AccountLoginEmailChangeRequestedHandler(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Moves the account's login to the new address, or refuses.</summary>
    /// <param name="message">The account and its new owner address.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Success, or <c>EmailTaken</c> when another login already holds the address.</returns>
    public async Task<Result<bool>> HandleAsync(
        AccountLoginEmailChangeRequested message,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.AccountId == message.AccountId, cancellationToken);
        if (user is null)
        {
            return Result<bool>.Ok(true);
        }

        var taken = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(other => other.Email == message.Email && other.Id != user.Id, cancellationToken);
        if (taken)
        {
            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.EmailTaken), ErrorType.Conflict));
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
            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.EmailTaken), ErrorType.Conflict));
        }

        return Result<bool>.Ok(true);
    }
}
