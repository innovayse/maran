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
/// Issues the panel login of a newly created reseller and sends its contact an invitation to set the
/// first password (<see cref="ResellerCreated"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The reseller's counterpart of <see cref="AccountCreatedHandler"/>, with one difference that
/// matters.</b> A reseller's login holds no membership of any account, so the key that makes a repeat
/// harmless is the reseller id on the login (<c>UX_Users_ResellerId</c>), not an owner membership.
/// A login already carrying the reseller's id means this is a repeat — a retried command, a resent
/// event — and the handler returns rather than creating a second login or a second live token.
/// </para>
/// <para>
/// <b>A unique violation is a race only when the reseller's login now exists.</b> The pre-check and
/// the insert are two round trips, so two concurrent deliveries can both read "no login yet"; the
/// loser then fails on the unique index and finds the winner's login. But the same SQL state is also
/// raised when the login NAME or the contact ADDRESS is already held by another login — an
/// administrator, a customer, a team member — and swallowing that would report a reseller created
/// with nobody able to sign in as it. So after a violation the handler asks again whether a login for
/// this reseller exists: if it does, the winner's invitation is the only one and this returns
/// quietly; if it does not, the failure is real and is rethrown, which the Accounts module reports as
/// <c>ResellerLoginIssuanceFailed</c> while the reseller row stands.
/// </para>
/// <para>
/// <b>The mail is PUBLISHED, never sent here</b>, on the local non-durable queue, for the reason
/// <see cref="AccountCreatedHandler"/> gives: the body carries a live token, and a durable queue would
/// rest it on disk. The token appears in the mail and nowhere else; it is never journalled, returned
/// or logged, and only its digest is stored.
/// </para>
/// <para>
/// <b>Written in the contact's language</b>, not the administrator's: the mail is composed with the
/// <see cref="ResellerCreated.OwnerLanguage"/> the event carries.
/// </para>
/// </remarks>
public sealed class ResellerCreatedHandler
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
    public ResellerCreatedHandler(
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

    /// <summary>Issues the reseller's login and publishes its invitation, unless a login already exists.</summary>
    /// <param name="message">The reseller that was just created.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Resolves once the login is stored and the mail is queued for sending.</returns>
    public async Task HandleAsync(ResellerCreated message, CancellationToken cancellationToken)
    {
        if (await LoginExistsAsync(message.ResellerId, cancellationToken))
        {
            // A repeat: a retried command, or this event announced twice. See the type's remarks.
            return;
        }

        var now = _clock.UtcNow;
        var user = User.InviteReseller(Guid.NewGuid(), message.Username, message.OwnerEmail, message.ResellerId, now);
        var token = InvitationTokenHasher.Generate();
        var invitationToken = new InvitationToken(Guid.NewGuid(), user.Id, InvitationTokenHasher.Hash(token), now);

        _dbContext.Users.Add(user);
        _dbContext.InvitationTokens.Add(invitationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Nothing this call added exists (SaveChanges rolled the batch back). Both entries are
            // detached rather than left tracked as pending adds on a context that may be reused.
            _dbContext.Entry(user).State = EntityState.Detached;
            _dbContext.Entry(invitationToken).State = EntityState.Detached;

            if (await LoginExistsAsync(message.ResellerId, cancellationToken))
            {
                // A concurrent delivery won the race and its login holds the live token.
                return;
            }

            // The name or the address belongs to another login: a real failure, not a race.
            throw;
        }

        await _journal.RecordIdentifiedAsync(
            user.Id,
            IdentityAuditActions.ResellerInvited,
            user.Username,
            ipAddress: string.Empty,
            userAgent: string.Empty,
            succeeded: true,
            cancellationToken);

        // Published, not invoked: the send happens in the Notifications module, on its own.
        await _bus.PublishAsync(_composer.ComposeForReseller(user.Email, token, message.OwnerLanguage));
    }

    /// <summary>Whether a login carrying the reseller's id already exists.</summary>
    /// <param name="resellerId">The reseller.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><c>true</c> when the reseller already has its login.</returns>
    private async Task<bool> LoginExistsAsync(Guid resellerId, CancellationToken cancellationToken)
    {
        return await _dbContext.Users.AnyAsync(login => login.ResellerId == resellerId, cancellationToken);
    }
}
