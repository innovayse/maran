using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Utilities.Tokens;
using Npgsql;
using Wolverine;

namespace Maran.Modules.Identity.Commands.ResendInvitation;

/// <summary>
/// Handles <see cref="ResendInvitationCommand"/>: retires every outstanding invitation token for a
/// hosting account's login and issues a new one, for an administrator who wants a lost or expired
/// invitation sent again — creating the login itself first, when <c>AccountCreatedHandler</c> never
/// managed to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this command exists rather than a plain "new token" endpoint.</b>
/// <c>AccountCreatedHandler</c> is invoked inline by the Accounts module and creates the login and
/// its first invitation token in one transaction; when that call fails after the account row is
/// already committed, the account is left with no login at all
/// (<see cref="Maran.Sdk.Events.AccountCreated"/>'s own remarks name this and name this command as
/// the recovery). This handler therefore starts by finding the login, not the token, and creates it
/// when it is missing — see the next paragraph for what makes that possible.
/// </para>
/// <para>
/// <b>The missing login is recreated from <c>Account.OwnerEmail</c>, not from the login itself.</b>
/// An earlier version of this handler could not do this: the owner's contact address used to be
/// stored nowhere but the very <see cref="User"/> row this handler is trying to recreate, and the
/// <see cref="Maran.Sdk.Events.AccountCreated"/> event that once carried it is invoked in-process
/// and never stored — so once <c>AccountCreatedHandler</c> had returned without writing the row,
/// the address was gone beyond recall. <c>Account.OwnerEmail</c> exists precisely to close that
/// gap: it is the account's own durable copy, read here through <see cref="IAccountDirectory"/> —
/// the one legal cross-module window (rules/architecture.md) — and used to create the login exactly
/// as <c>AccountCreatedHandler</c> would have: same <see cref="UserState.Invited"/> state, a fresh
/// token, the same published mail.
/// </para>
/// <para>
/// <b>The login insert races the identical way <c>AccountCreatedHandler</c>'s does, and is caught
/// the identical way.</b> Two administrators resending at once — or one double-click — can both read
/// "no login for this account" before either writes one; only <c>UX_Users_AccountId</c>, the same
/// partial unique index <c>AccountCreatedHandler</c>'s own remarks name, actually stops the second
/// row. The loser's insert therefore fails with a unique-violation <see cref="DbUpdateException"/>
/// rather than a check it could have made in advance, so it is caught exactly where
/// <c>AccountCreatedHandler</c> catches it, and for the same reason: the winner's login already
/// exists and its own request will issue and mail its own token, so there is nothing left for the
/// loser to do but say so. Unlike <c>AccountCreatedHandler</c> — an event handler with no caller
/// waiting on an answer — this one has an administrator waiting on a result, so the loser answers
/// success rather than merely returning: an invitation is, or is about to be, on its way, which is
/// the true state of the account either way.
/// </para>
/// <para>
/// <b>Refuses a login that is not <see cref="UserState.Invited"/>, not only an
/// <see cref="UserState.Active"/> one.</b> The brief for this feature names <c>Active</c>
/// specifically — a fresh token for a live account is a password reset in disguise that bypasses
/// <c>reset-password</c>'s own rate limit — but a <see cref="UserState.Suspended"/> login was
/// <c>Active</c> before it was suspended, so it already has a real password too:
/// <see cref="User.Activate"/> would overwrite it and unconditionally return the login to
/// <c>Active</c>, silently lifting the suspension. Refusing on anything other than
/// <c>Invited</c> closes both cases with one check.
/// </para>
/// </remarks>
public sealed class ResendInvitationCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The bus the invitation mail is published on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Records the resend.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Renders the invitation mail, shared with <c>AccountCreatedHandler</c>.</summary>
    private readonly InvitationMailComposer _composer;

    /// <summary>
    /// The read-only window onto the Accounts module: the account's system user name and its own
    /// contact address, needed to recreate a login that was never successfully created.
    /// </summary>
    private readonly IAccountDirectory _accountDirectory;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>
    /// The authenticated administrator issuing this resend — the actor the journal entry must name,
    /// not the invited customer the resend is about.
    /// </summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="bus">The bus the invitation mail is published on.</param>
    /// <param name="journal">Records the resend.</param>
    /// <param name="composer">Renders the invitation mail.</param>
    /// <param name="accountDirectory">The read-only window onto the Accounts module.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="currentUser">The authenticated administrator issuing the resend.</param>
    public ResendInvitationCommandHandler(
        IdentityDbContext dbContext,
        IMessageBus bus,
        IdentityAuditJournal journal,
        InvitationMailComposer composer,
        IAccountDirectory accountDirectory,
        IClock clock,
        ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _bus = bus;
        _journal = journal;
        _composer = composer;
        _accountDirectory = accountDirectory;
        _clock = clock;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Creates the account's login if it is missing, retires every outstanding token for it, and
    /// issues a new one.
    /// </summary>
    /// <param name="command">The account whose owner is being (re)invited.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// Success once the mail is queued — or once a concurrent resend has already queued one, see the
    /// type's remarks — or a typed refusal: no such account, or a login that already has a password.
    /// </returns>
    public async Task<Result<bool>> HandleAsync(ResendInvitationCommand command, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var user = await _dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.AccountId == command.AccountId, cancellationToken);

        if (user is null)
        {
            var (accountExists, created) = await CreateMissingLoginAsync(command.AccountId, now, cancellationToken);
            if (!accountExists)
            {
                return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.InvitationAccountNotFound), ErrorType.NotFound));
            }

            if (created is null)
            {
                // Lost the race to create the login — see the type's remarks. The winner's own
                // request issues and mails its own token, so there is nothing left to do here.
                return Result<bool>.Ok(true);
            }

            user = created;
        }
        else if (user.State != UserState.Invited)
        {
            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.InvitationLoginNotPending), ErrorType.Conflict));
        }

        foreach (var outstanding in await _dbContext.InvitationTokens
            .Where(existing => existing.UserId == user.Id && existing.UsedAt == null)
            .ToListAsync(cancellationToken))
        {
            outstanding.Consume(now);
        }

        var token = InvitationTokenHasher.Generate();
        _dbContext.InvitationTokens.Add(
            new InvitationToken(Guid.NewGuid(), user.Id, InvitationTokenHasher.Hash(token), now));

        await _dbContext.SaveChangesAsync(cancellationToken);

        // The actor is the ADMINISTRATOR who asked for the resend, not the invited customer the
        // resend is about — RecordIdentifiedAsync only names the actor when its id matches
        // ICurrentUser, so passing the administrator's own id here is what makes the entry read
        // "administrator X resent an invitation to customer Y" rather than naming the customer as
        // having verified themselves, which they never did.
        await _journal.RecordIdentifiedAsync(
            _currentUser.UserId,
            AuditActions.CustomerInvited,
            user.Username,
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        // Published, not invoked, for the same reason AccountCreatedHandler publishes rather than
        // sends: the credential lives in the Notifications module, out of this module's reach.
        await _bus.PublishAsync(_composer.Compose(user.Email, token));

        return Result<bool>.Ok(true);
    }

    /// <summary>
    /// Creates the account's login exactly as <c>AccountCreatedHandler</c> would have, from the
    /// account's own directory entry.
    /// </summary>
    /// <param name="accountId">The account whose login is missing.</param>
    /// <param name="now">The current instant, taken from <see cref="IClock"/>.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// <c>(false, null)</c> when no such account exists; <c>(true, null)</c> when the account exists
    /// but a concurrent call already won the race to create its login (see the type's remarks); or
    /// <c>(true, user)</c> with the newly created, saved login.
    /// </returns>
    private async Task<(bool AccountExists, User? User)> CreateMissingLoginAsync(
        Guid accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var account = await _accountDirectory.FindAsync(accountId, cancellationToken);
        if (account is null)
        {
            return (false, null);
        }

        // OwnerEmail is null only for an account that predates the column (Account.OwnerEmail's own
        // remarks) — and such an account has had every opportunity, over that whole time, for
        // AccountCreatedHandler to have created its login already. A null reaching here with no
        // login on top of it is an operational anomaly this command cannot repair by guessing an
        // address, so it fails loudly rather than mailing an invitation to a fabricated one.
        var ownerEmail = account.OwnerEmail
            ?? throw new InvalidOperationException(
                $"Account {accountId} has no login and no recorded owner e-mail; it predates " +
                "Account.OwnerEmail and cannot be recovered by resending an invitation.");

        var user = User.Invite(Guid.NewGuid(), account.Username, ownerEmail, accountId, now);
        _dbContext.Users.Add(user);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent resend (or a double-click) won the race between the lookup in
            // HandleAsync and this insert — UX_Users_AccountId is what actually stopped the second
            // row, not that lookup. Nothing this call added exists (SaveChanges rolled the insert
            // back), so the entity is detached rather than left tracked as a still-pending add on a
            // context the rest of this handler keeps using.
            _dbContext.Entry(user).State = EntityState.Detached;
            return (true, null);
        }

        return (true, user);
    }
}
