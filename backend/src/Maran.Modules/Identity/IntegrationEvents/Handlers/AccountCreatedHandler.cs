using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Maran.SharedKernel.Utilities.Tokens;
using Npgsql;
using Wolverine;

namespace Maran.Modules.Identity.IntegrationEvents.Handlers;

/// <summary>
/// Issues the panel login of a newly created hosting account and sends its owner an invitation.
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
/// both read "no login yet" before either writes. Only <c>UX_Users_AccountId</c>, the partial unique
/// index on <c>User.AccountId</c> (<c>UserConfiguration</c>), actually stops the second row from
/// being created; the pre-check is merely the path that avoids paying for the race on the ordinary,
/// uncontended call. A doc comment that called this idempotent on the strength of the check alone
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

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Identity module's database context.</param>
    /// <param name="bus">The bus the invitation mail is published on.</param>
    /// <param name="journal">Records the invitation.</param>
    /// <param name="composer">Renders the invitation mail.</param>
    /// <param name="clock">The panel's clock.</param>
    public AccountCreatedHandler(
        IdentityDbContext dbContext,
        IMessageBus bus,
        IdentityAuditJournal journal,
        InvitationMailComposer composer,
        IClock clock)
    {
        _dbContext = dbContext;
        _bus = bus;
        _journal = journal;
        _composer = composer;
        _clock = clock;
    }

    /// <summary>Issues the account owner's login and publishes their invitation, unless one already exists.</summary>
    /// <param name="message">The account that was just created.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Resolves once the login is stored and the mail is queued for sending.</returns>
    public async Task HandleAsync(AccountCreated message, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Users
            .AnyAsync(candidate => candidate.AccountId == message.AccountId, cancellationToken);
        if (existing)
        {
            // A repeat: a retried command, or this event announced twice. Two live invitations to
            // one account would be two keys, and the owner has no way to know which the panel will
            // still honour, so this returns quietly rather than creating a second one. This is the
            // ordinary path; the genuinely concurrent case is caught below because this check alone
            // cannot close the race — see the type's remarks.
            return;
        }

        var now = _clock.UtcNow;
        var user = User.Invite(Guid.NewGuid(), message.Username, message.OwnerEmail, message.AccountId, now);
        var token = InvitationTokenHasher.Generate();

        var invitationToken = new InvitationToken(
            Guid.NewGuid(), user.Id, InvitationTokenHasher.Hash(token), now);
        _dbContext.Users.Add(user);
        _dbContext.InvitationTokens.Add(invitationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent delivery of AccountCreated won the race between the check above and this
            // insert, and its row already owns the login and the live token — UX_Users_AccountId is
            // what actually stopped the second row, not the check. Nothing this call added exists
            // (SaveChanges rolled the whole batch back), there is no orphaned token to clean up, and
            // there is no second mail to send: the winner's invitation is the only one, so this
            // returns quietly exactly as the pre-check path does. Both entries are detached rather
            // than left tracked as still-pending adds on a context that may be reused.
            _dbContext.Entry(user).State = EntityState.Detached;
            _dbContext.Entry(invitationToken).State = EntityState.Detached;
            return;
        }

        await _journal.RecordIdentifiedAsync(
            user.Id,
            AuditActions.CustomerInvited,
            user.Username,
            ipAddress: string.Empty,
            userAgent: string.Empty,
            succeeded: true,
            cancellationToken);

        // Published, not invoked. The send happens in the Notifications module, on its own — see the
        // type's remarks.
        await _bus.PublishAsync(_composer.Compose(user.Email, token));
    }
}
