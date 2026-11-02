using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Extensions;
using Maran.Modules.Identity.Mappers;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Maran.Sdk.Interfaces;
using Npgsql;
using Wolverine;

namespace Maran.Modules.Identity.Commands.InviteSubUser;

/// <summary>
/// Handles <see cref="InviteSubUserCommand"/>: creates the new person's login (without a password),
/// their membership with the chosen features, and a one-time invitation that is mailed to them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The person sets their own password.</b> The owner never learns it and is never handed a link:
/// the invitation is the same token an account owner receives, minted by the same
/// <see cref="InvitationIssuer"/>, mailed through the same composer, and accepted through the same
/// <c>AcceptInvitationCommandHandler</c>. Nothing here stores, logs or journals the token.
/// </para>
/// <para>
/// <b>The membership exists from the invitation, not from its acceptance.</b> The login is
/// <c>Invited</c> until the password is chosen and an invited login cannot sign in, so a membership
/// that exists early grants nothing; it does mean the owner sees the pending person in the list and
/// can limit or withdraw them before they ever accept, and that acceptance needs no knowledge of
/// accounts at all.
/// </para>
/// <para>
/// <b>A login that already exists is refused, not joined.</b> Adding an existing person to a second
/// account without their say would put an account in their switcher unasked, and sending them an
/// invitation they have no use for would be a token that sets a password they already have. The owner
/// is told the address or name is taken; joining existing people is a separate, consenting flow.
/// </para>
/// <para>
/// <b>Uniqueness is decided by the database.</b> The pre-checks give the owner a precise answer in the
/// common case; <c>IX_Users_Username</c> and <c>IX_Users_Email</c> are what actually stop a race, and a
/// unique violation is translated into the same two answers.
/// </para>
/// </remarks>
public sealed class InviteSubUserCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Establishes that the caller owns the account they are acting for.</summary>
    private readonly SubUserOwnerGuard _guard;

    /// <summary>Retires old tokens and mints the new one.</summary>
    private readonly InvitationIssuer _issuer;

    /// <summary>Writes the invitation mail.</summary>
    private readonly InvitationMailComposer _composer;

    /// <summary>The read-only window onto the Accounts module: the account's state and language.</summary>
    private readonly IAccountDirectory _accountDirectory;

    /// <summary>Publishes the mail for the Notifications module to send.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Records the invitation.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>The authenticated owner — the actor the journal entry names.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="guard">Establishes that the caller owns the account they are acting for.</param>
    /// <param name="issuer">Retires old tokens and mints the new one.</param>
    /// <param name="composer">Writes the invitation mail.</param>
    /// <param name="accountDirectory">The read-only window onto the Accounts module.</param>
    /// <param name="bus">Publishes the mail.</param>
    /// <param name="journal">Records the invitation.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="currentUser">The authenticated owner.</param>
    public InviteSubUserCommandHandler(
        IdentityDbContext dbContext,
        SubUserOwnerGuard guard,
        InvitationIssuer issuer,
        InvitationMailComposer composer,
        IAccountDirectory accountDirectory,
        IMessageBus bus,
        IdentityAuditJournal journal,
        IClock clock,
        ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _guard = guard;
        _issuer = issuer;
        _composer = composer;
        _accountDirectory = accountDirectory;
        _bus = bus;
        _journal = journal;
        _clock = clock;
        _currentUser = currentUser;
    }

    /// <summary>Adds the person and mails their invitation.</summary>
    /// <param name="command">The new person's login name, address and features.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The new sub-user, or a typed refusal.</returns>
    public async Task<Result<SubUserDto>> HandleAsync(InviteSubUserCommand command, CancellationToken cancellationToken)
    {
        var owned = await _guard.RequireOwnedAccountAsync(cancellationToken);
        if (!owned.IsSuccess)
        {
            return await RefuseAsync(command, owned.Error!, cancellationToken);
        }

        var account = await _accountDirectory.FindAsync(owned.Value, cancellationToken);
        if (account is null)
        {
            return await RefuseAsync(
                command, Error.Of(nameof(ErrorMessages.InvitationAccountNotFound), ErrorType.NotFound), cancellationToken);
        }

        if (account.State == AccountState.Suspended)
        {
            // A suspended account is offered no way in for anybody (InvitationIssuer's remarks).
            return await RefuseAsync(
                command, Error.Of(nameof(ErrorMessages.InvitationAccountSuspended), ErrorType.Conflict), cancellationToken);
        }

        var taken = await FindTakenAsync(command, cancellationToken);
        if (taken is not null)
        {
            return await RefuseAsync(command, taken, cancellationToken);
        }

        var now = _clock.UtcNow;
        var user = User.InviteMember(Guid.NewGuid(), command.Username, command.Email, now);
        var membership = AccountMembership.Admit(
            Guid.NewGuid(), user.Id, owned.Value, command.Features.ToSet(), now);
        _dbContext.Users.Add(user);
        _dbContext.AccountMemberships.Add(membership);

        MintedInvitationToken minted;
        try
        {
            // Saves the login, the membership and the token's digest in one transaction.
            minted = await _issuer.MintAsync(user, now, cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation)
        {
            // Nothing this call added exists (SaveChanges rolled it back); stop tracking it so a
            // retry on this context starts clean.
            _dbContext.ChangeTracker.Clear();
            var raced = Error.Of(
                violation.ConstraintName == "IX_Users_Email"
                    ? nameof(ErrorMessages.SubUserEmailTaken)
                    : nameof(ErrorMessages.SubUserLoginTaken),
                ErrorType.Conflict);
            return await RefuseAsync(command, raced, cancellationToken);
        }

        await _journal.RecordIdentifiedAsync(
            _currentUser.RequirePrincipal().UserId,
            AuditActions.SubUserInvited,
            user.Username,
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        // Published, not sent, because the mail credential lives in the Notifications module. The
        // token is in the message body for this one hop and is stored nowhere (the message is not
        // durable: rules/csharp.md "a message carrying a secret is never durable").
        await _bus.PublishAsync(_composer.Compose(user.Email, minted.Token.Reveal(), account.OwnerLanguage));

        return Result<SubUserDto>.Ok(SubUserMapper.From(user, membership));
    }

    /// <summary>Looks for a login that already holds the requested name or address.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The conflict to answer with, or <c>null</c> when both are free.</returns>
    private async Task<Error?> FindTakenAsync(InviteSubUserCommand command, CancellationToken cancellationToken)
    {
        if (await _dbContext.Users.AnyAsync(candidate => candidate.Username == command.Username, cancellationToken))
        {
            return Error.Of(nameof(ErrorMessages.SubUserLoginTaken), ErrorType.Conflict);
        }

        if (await _dbContext.Users.AnyAsync(candidate => candidate.Email == command.Email, cancellationToken))
        {
            return Error.Of(nameof(ErrorMessages.SubUserEmailTaken), ErrorType.Conflict);
        }

        return null;
    }

    /// <summary>Journals a refused attempt and answers with the refusal.</summary>
    /// <param name="command">The refused request.</param>
    /// <param name="error">Why it was refused.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<SubUserDto>> RefuseAsync(
        InviteSubUserCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        // The subject is the login name the owner asked for: a refused attempt still leaves a trace of
        // what was attempted, as the other credential-issuing actions do.
        await _journal.RecordIdentifiedAsync(
            _currentUser.RequirePrincipal().UserId,
            AuditActions.SubUserInvited,
            command.Username,
            command.IpAddress,
            command.UserAgent,
            succeeded: false,
            cancellationToken);

        return Result<SubUserDto>.Fail(error);
    }
}
