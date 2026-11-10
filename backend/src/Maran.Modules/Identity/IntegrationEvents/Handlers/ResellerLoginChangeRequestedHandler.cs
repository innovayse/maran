using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Npgsql;

namespace Maran.Modules.Identity.IntegrationEvents.Handlers;

/// <summary>
/// Moves a reseller's login to the name and contact address an administrator just gave the reseller
/// (<see cref="ResellerLoginChangeRequested"/>), so it signs in with the new name and its mails go to
/// the new address.
/// </summary>
/// <remarks>
/// <para>
/// <b>A name or an address another login holds is refused, not shared</b> (<c>UsernameTaken</c>,
/// <c>EmailTaken</c>), as <see cref="AccountLoginEmailChangeRequestedHandler"/> refuses an address:
/// a sign-in is by name and a password reset by address, so two logins on one of them would make
/// "whose is this" a question with two answers. <c>IX_Users_Username</c> and <c>IX_Users_Email</c>
/// are the backstops: a second change claiming the same value in the same instant fails its save with
/// <c>SqlState 23505</c> and is answered with the refusal of the field that clashed.
/// </para>
/// <para>
/// <b>No login is a success</b>, and so is a login that already carries the name and the address.
/// Sessions are left alone: a rename is not a credential change, and a session is keyed by the login's
/// id, not by its name.
/// </para>
/// <para>
/// <b>The invitation and reset tokens mailed to the address being left are retired in the same
/// save</b>, for the reason <see cref="AccountLoginEmailChangeRequestedHandler"/> gives: the old
/// mailbox must not keep a key to the reseller's login.
/// </para>
/// <para>
/// <b>The change itself is journaled by the Accounts module</b>, with the administrator who asked and
/// from where; this handler writes only the entry for the tokens it retired.
/// </para>
/// </remarks>
public sealed class ResellerLoginChangeRequestedHandler
{
    /// <summary>The Identity module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Retires the invitation and reset tokens mailed to the address being left.</summary>
    private readonly CredentialTokenRetirement _tokenRetirement;

    /// <summary>Records the retirement of those tokens.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Identity module's database context.</param>
    /// <param name="tokenRetirement">Retires the tokens mailed to the old address.</param>
    /// <param name="journal">Records the retirement.</param>
    /// <param name="clock">The panel's clock.</param>
    public ResellerLoginChangeRequestedHandler(
        IdentityDbContext dbContext,
        CredentialTokenRetirement tokenRetirement,
        IdentityAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _tokenRetirement = tokenRetirement;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Moves the reseller's login to the new name and address, or refuses.</summary>
    /// <param name="message">The reseller and the name and address its login is to carry.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// Success, <c>UsernameTaken</c> when another login holds the name, or <c>EmailTaken</c> when
    /// another login holds the address.
    /// </returns>
    public async Task<Result> HandleAsync(ResellerLoginChangeRequested message, CancellationToken cancellationToken)
    {
        var login = await _dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.ResellerId == message.ResellerId, cancellationToken);
        if (login is null)
        {
            return Result.Ok();
        }

        var nameTaken = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(other => other.Username == message.Username && other.Id != login.Id, cancellationToken);
        if (nameTaken)
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.UsernameTaken), ErrorType.Conflict));
        }

        var emailTaken = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(other => other.Email == message.Email && other.Id != login.Id, cancellationToken);
        if (emailTaken)
        {
            return Result.Fail(Error.Of(nameof(ErrorMessages.EmailTaken), ErrorType.Conflict));
        }

        // Only a real move retires anything: keeping the address leaves its mail where it was sent.
        var retired = 0;
        if (!string.Equals(login.Email, message.Email, StringComparison.Ordinal))
        {
            retired = await _tokenRetirement.RetireAsync(login.Id, _clock.UtcNow, cancellationToken);
        }

        login.Rename(message.Username);
        login.ChangeEmail(message.Email);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation)
        {
            // The ONE narrowing: only 23505 means another login claimed the name or the address between
            // the checks above and this save. The change is undone so the context is not left dirty.
            _dbContext.Entry(login).State = EntityState.Detached;

            var code = violation.ConstraintName == "IX_Users_Username"
                ? nameof(ErrorMessages.UsernameTaken)
                : nameof(ErrorMessages.EmailTaken);
            return Result.Fail(Error.Of(code, ErrorType.Conflict));
        }

        if (retired > 0)
        {
            await _journal.RecordSystemAsync(
                IdentityAuditActions.CredentialTokensRetired, $"{login.Id}: {retired}", succeeded: true, cancellationToken);
        }

        return Result.Ok();
    }
}
