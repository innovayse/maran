using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Maran.SharedKernel.Security;
using Maran.SharedKernel.Utilities.Tokens;
using Wolverine;

namespace Maran.Modules.Identity.IntegrationEvents.Handlers;

/// <summary>
/// Issues the panel login of a newly created hosting account: by default with an invitation mailed to its
/// owner, or — when the creator chose an initial password — with that password and no invitation at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>Idempotent, and backed by a constraint rather than by timing.</b> A login already bound to
/// the account means this is a repeat — a retried command, a resent event — and the handler returns
/// rather than creating a second login or a second live token. Two live invitations to one account
/// are two keys, and the owner cannot know which the panel will still honour.
/// </para>
/// <para>
/// <b>The pre-check is the ordinary path; the catch below is the truthful one.</b> The
/// <c>AnyAsync</c> check and the insert are two separate round trips, so two concurrent deliveries of
/// <see cref="AccountCreated"/> — the exact "retried command, resent event" cases named above — can
/// both read "no login yet" before either writes. Only <c>UX_AccountMemberships_AccountId_Owner</c>,
/// the partial unique index on the owner membership (<c>AccountMembershipConfiguration</c>), actually
/// stops the second row from being created; the pre-check is merely the path that avoids paying for
/// the race on the ordinary, uncontended call. A doc comment that called this idempotent on the strength of the check alone
/// would be describing timing, not a guarantee — the loser of the race still has to survive losing
/// it, which is what the <c>catch</c> in <see cref="HandleAsync"/> is for.
/// </para>
/// <para>
/// <b>The mail is PUBLISHED, never sent here.</b> The SMTP credential lives in the Notifications
/// module and is deliberately out of reach (rules/architecture.md "shared facility"). The queue is
/// local and non-durable on purpose: the body carries a live token, and a durable queue would rest
/// it on disk and outlive its own lifetime. A process that dies between the publish and the send
/// loses the mail, and the administrator resends.
/// </para>
/// <para>
/// <b>The initial-password branch (<see cref="OwnerLoginMode.InitialPassword"/>).</b> The login is made
/// with <see cref="User.CreateWithPassword"/>: usable at once, and marked so that its first token can do
/// nothing but change the password (<c>PasswordChangeCompleteRequirement</c>). No invitation token is
/// created and no mail is published — the creator hands the password over out of band, which is the
/// whole of the use case. The password arrives as a <see cref="Maran.SharedKernel.Security.SensitiveString"/> and is revealed in ONE
/// place, to be hashed with Argon2id before the first save; it is never journalled, logged or put in an
/// exception message. Its strength is checked HERE, once, against the same policy every other path that
/// sets a password applies (<see cref="InitialPasswordPolicy"/>, which the Accounts module also asks before it provisions anything): a password
/// that fails is refused before any row is added, so no login exists to be guessed.
/// </para>
/// <para>
/// <b>The mode and the password must agree, and a mismatch is refused, not repaired.</b> A password with
/// <see cref="OwnerLoginMode.Invitation"/> is never silently ignored and a missing one with
/// <see cref="OwnerLoginMode.InitialPassword"/> never falls through to a passwordless invitation: either
/// would leave the creator believing in a login the panel did not make.
/// </para>
/// <para>
/// <b>Only the owner-membership index is a repeat.</b> Of the unique indexes a save can break, just
/// <c>UX_AccountMemberships_AccountId_Owner</c> means a concurrent delivery already made this login
/// (<see cref="ConcurrentOwnerLoginPolicy"/>). <c>IX_Users_Email</c> and <c>IX_Users_Username</c> mean
/// another login holds the value; that is rethrown and answered as <c>AccountLoginIssuanceFailed</c>. The
/// Accounts module asks <see cref="Maran.Sdk.Interfaces.IOwnerLoginDirectory"/> first and refuses with a
/// typed code before provisioning, so this is the backstop for a clash that appears in between.
/// </para>
/// <para>
/// <b>Thrown here reaches the API caller as a typed failure, not a generic one.</b>
/// <see cref="AccountCreated"/> is invoked inline by the Accounts module, which wraps the call in a
/// try/catch: anything this handler throws is logged there and answered as
/// <c>AccountLoginIssuanceFailed</c>, naming the fact that the account row already exists and that
/// "resend invitation" — not creating the account again — is the recovery. Ordinary conditions — the
/// account already having a login — are therefore handled by returning quietly, not by throwing.
/// </para>
/// <para>
/// <b>The token appears in the mail and nowhere else.</b> It is never passed to the audit journal,
/// never returned, and never logged — <see cref="InvitationTokenHasher.Hash(string)"/> is stored in
/// its place, and even that digest never reaches a log line.
/// </para>
/// <para>
/// <b>That is why a panel that cannot send mail is not a dead end.</b> On an installation whose SMTP
/// is not configured this mail is skipped, and this token is then unrecoverable by design: nothing
/// stores it. The way back in is not to keep a copy but to replace it — the administrator creates a
/// fresh one-time link from the account's page (<c>IssueInvitationLinkCommandHandler</c>), which
/// retires this token and hands the new plaintext over once.
/// </para>
/// </remarks>
public sealed class AccountCreatedHandler
{
    /// <summary>The Identity module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The bus the invitation mail is published on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Records the invitation, once issued.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Renders the invitation mail.</summary>
    private readonly InvitationMailComposer _composer;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Tells whether the account already has an owner login.</summary>
    private readonly AccountOwnerFinder _owners;

    /// <summary>Hashes the initial password with Argon2id.</summary>
    private readonly IPasswordHasher _passwordHasher;

    /// <summary>The panel's one verdict on an initial password.</summary>
    private readonly InitialPasswordPolicy _passwordPolicy;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Identity module's database context.</param>
    /// <param name="owners">Tells whether the account already has an owner login.</param>
    /// <param name="bus">The bus the invitation mail is published on.</param>
    /// <param name="journal">Records the invitation.</param>
    /// <param name="composer">Renders the invitation mail.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="passwordHasher">Hashes the initial password.</param>
    /// <param name="passwordPolicy">The panel's one verdict on an initial password.</param>
    public AccountCreatedHandler(
        IdentityDbContext dbContext,
        AccountOwnerFinder owners,
        IMessageBus bus,
        IdentityAuditJournal journal,
        InvitationMailComposer composer,
        IClock clock,
        IPasswordHasher passwordHasher,
        InitialPasswordPolicy passwordPolicy)
    {
        _dbContext = dbContext;
        _owners = owners;
        _bus = bus;
        _journal = journal;
        _composer = composer;
        _clock = clock;
        _passwordHasher = passwordHasher;
        _passwordPolicy = passwordPolicy;
    }

    /// <summary>
    /// Issues the account owner's login — with an invitation, or with the initial password — unless one
    /// already exists.
    /// </summary>
    /// <param name="message">The account that was just created.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Resolves once the login is stored and, for an invitation, the mail is queued for sending.</returns>
    /// <exception cref="InvalidOperationException">
    /// The mode and the password disagree, or the initial password does not meet the panel's password
    /// policy. Nothing is stored; the account exists without a login and the invitation resend is the
    /// recovery (see the type's remarks). The message names the rule that failed and never the password.
    /// </exception>
    public async Task HandleAsync(AccountCreated message, CancellationToken cancellationToken)
    {
        var initialPassword = RequireCoherentPassword(message);

        if (await _owners.ExistsAsync(message.AccountId, cancellationToken))
        {
            // A repeat: a retried command, or this event announced twice. Two live invitations to
            // one account would be two keys, and the owner has no way to know which the panel will
            // still honour, so this returns quietly rather than creating a second one. This is the
            // ordinary path; the genuinely concurrent case is caught below because this check alone
            // cannot close the race — see the type's remarks.
            return;
        }

        if (initialPassword is not null)
        {
            await RequireAcceptablePasswordAsync(message.InitialPassword!, message.Username, cancellationToken);
        }

        var now = _clock.UtcNow;

        // Exactly one of the two ways in. An invitation leaves a login with no password and one live
        // token; an initial password leaves a usable login with no token at all.
        User user;
        InvitationToken? invitationToken = null;
        string? token = null;
        if (initialPassword is null)
        {
            user = User.Invite(Guid.NewGuid(), message.Username, message.OwnerEmail, now);
            token = InvitationTokenHasher.Generate();
            invitationToken = new InvitationToken(Guid.NewGuid(), user.Id, InvitationTokenHasher.Hash(token), now);
        }
        else
        {
            user = User.CreateWithPassword(
                Guid.NewGuid(),
                message.Username,
                message.OwnerEmail,
                _passwordHasher.Hash(initialPassword),
                now);
        }

        // The owner's membership is written in the same save as the login: memberships are the
        // truth about which accounts a login may act for, so a login without one could sign in and
        // find no account to speak for.
        var membership = AccountMembership.Own(Guid.NewGuid(), user.Id, message.AccountId, now);
        _dbContext.Users.Add(user);
        _dbContext.AccountMemberships.Add(membership);
        if (invitationToken is not null)
        {
            _dbContext.InvitationTokens.Add(invitationToken);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            ConcurrentOwnerLoginPolicy.IsConcurrentDuplicate(exception.InnerException))
        {
            // Only the owner-membership index means this (any other unique violation — the e-mail
            // address or the name already held by another login — is rethrown, so the creator is told
            // the login was not made instead of reading a success). A concurrent delivery of AccountCreated won the race between the check above and this
            // insert, and its row already owns the login and the live token — UX_AccountMemberships_AccountId_Owner
            // is what actually stopped the second row, not the check. Nothing this call added exists
            // (SaveChanges rolled the whole batch back), there is no orphaned token to clean up, and
            // there is no second mail to send: the winner's invitation is the only one, so this
            // returns quietly exactly as the pre-check path does. Every entry is detached rather
            // than left tracked as still-pending adds on a context that may be reused.
            _dbContext.Entry(user).State = EntityState.Detached;
            _dbContext.Entry(membership).State = EntityState.Detached;
            if (invitationToken is not null)
            {
                _dbContext.Entry(invitationToken).State = EntityState.Detached;
            }

            return;
        }

        await _journal.RecordIdentifiedAsync(
            user.Id,
            token is null ? IdentityAuditActions.CustomerCreatedWithPassword : IdentityAuditActions.CustomerInvited,
            user.Username,
            ipAddress: string.Empty,
            userAgent: string.Empty,
            succeeded: true,
            cancellationToken);

        if (token is null || message.SuppressInvitationMail)
        {
            // The initial-password branch sends nothing: the creator hands the password over. An imported
            // account sends nothing either: the owner is invited when the administrator issues the link at
            // cut-over, never to a server that is not live yet (issue #127).
            return;
        }

        // Published, not invoked. The send happens in the Notifications module, on its own — see the
        // type's remarks.
        await _bus.PublishAsync(_composer.Compose(user.Email, token, message.OwnerLanguage));
    }

    /// <summary>
    /// Reads the initial password out of the event, after checking that the mode and the password agree.
    /// </summary>
    /// <param name="message">The event.</param>
    /// <returns>The password when the mode is the initial-password one; null for an invitation.</returns>
    /// <exception cref="InvalidOperationException">The password is missing or blank for the initial-password mode, or present for the invitation mode.</exception>
    private static string? RequireCoherentPassword(AccountCreated message)
    {
        var revealed = message.InitialPassword?.Reveal();

        if (message.LoginMode == OwnerLoginMode.Invitation)
        {
            if (message.InitialPassword is not null)
            {
                throw new InvalidOperationException(
                    "An initial password was supplied for an account whose owner is to be invited; no login was issued.");
            }

            return null;
        }

        if (string.IsNullOrWhiteSpace(revealed))
        {
            throw new InvalidOperationException(
                "The initial-password mode was chosen without a password; no login was issued.");
        }

        return revealed;
    }

    /// <summary>Applies the panel's password policy to an initial password, before any row is added.</summary>
    /// <param name="password">The initial password.</param>
    /// <param name="username">The login name, which a password may not equal.</param>
    /// <param name="cancellationToken">Cancels the policy read.</param>
    /// <returns>Resolves when the password is acceptable.</returns>
    /// <exception cref="InvalidOperationException">The password is too long, shorter than the operator's minimum, or equal to the username. The message never contains the password.</exception>
    private async Task RequireAcceptablePasswordAsync(
        SensitiveString password,
        string username,
        CancellationToken cancellationToken)
    {
        if (await _passwordPolicy.JudgeAsync(password, username, cancellationToken) != InitialPasswordVerdict.Accepted)
        {
            throw new InvalidOperationException(
                "The initial password does not meet the panel's password policy; no login was issued.");
        }
    }
}
