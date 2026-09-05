using System.Globalization;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Tokens;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Wolverine;

namespace Maran.Modules.Identity.Commands.RequestPasswordReset;

/// <summary>
/// Handles <see cref="RequestPasswordResetCommand"/>: issues a reset token for an address that
/// belongs to somebody, publishes the mail, and answers the caller identically either way.
/// </summary>
/// <remarks>
/// <para>
/// <b>The deliverable of this handler is a NON-property of its answer.</b> A caller must not be able
/// to tell, from the response, whether the address they typed belongs to an account. Three things
/// have to hold at once for that, and each is written here rather than assumed:
/// </para>
/// <para>
/// <b>(1) The same status and the same body.</b> There is one <c>return</c> statement. A branch that
/// returned a different <see cref="Result{T}"/> for an unknown address — even a "success" with a
/// different shape — is an oracle anybody can read with a browser's network tab.
/// </para>
/// <para>
/// <b>(2) The same work, as far as it can be made the same.</b> The token is generated and hashed
/// before the lookup's outcome is branched on, so the CSPRNG draw and the digest are paid for on
/// both paths. What is genuinely not symmetric is the database write, and this task's report states
/// that plainly rather than claiming a constant-time endpoint: a known address costs one extra row
/// insert inside the single <c>SaveChanges</c> the journal entry performs anyway. That is a
/// microsecond-scale difference on a network endpoint that is rate-limited per address, not the
/// seconds-scale one an inline mail send would create.
/// </para>
/// <para>
/// <b>(3) The mail is PUBLISHED, never sent here (R11).</b> This is the constraint the whole shape
/// exists for, and an earlier draft got it wrong in a way that was worse than the problem it was
/// fixing. Sending inline means the known-address path waits for a full SMTP round trip to somebody
/// else's server — seconds when it is slow, a timeout when it is broken — while the unknown-address
/// path returns immediately. That is an account-enumeration oracle readable with a stopwatch, by
/// anybody, at any distance. So the token is generated, the message is published to the panel's
/// LOCAL, NON-DURABLE queue, and this returns; the Notifications module sends afterwards, on its own.
/// </para>
/// <para>
/// <b>The queue is non-durable, and that is also a security property.</b> The body carries a live
/// token — permission to become the account. A durable queue would write it into an envelope table,
/// where it would rest on disk, appear in a database dump, and outlive its own hour. The cost is
/// stated and accepted: a process that dies between the publish and the send loses the mail, and the
/// user asks again.
/// </para>
/// <para>
/// <b>Outstanding tokens are retired first.</b> Asking for a reset twice must not leave two live
/// keys to the account: the older mail may be the one an attacker intercepted, and the user has no
/// way to know which of the two the panel will still honour.
/// </para>
/// </remarks>
public sealed class RequestPasswordResetCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The bus the mail request is published on. Never invoked — see the type's remarks.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Records the request, whether or not the address belongs to anybody.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The message text, in the recipient's language.</summary>
    private readonly IStringLocalizer<EmailTemplates> _templates;

    /// <summary>The panel's own public address, for the link in the mail.</summary>
    private readonly PasswordResetOptions _options;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="bus">The bus the mail request is published on.</param>
    /// <param name="journal">Records the request.</param>
    /// <param name="templates">The message text, in the recipient's language.</param>
    /// <param name="options">The panel's own public address.</param>
    /// <param name="clock">The panel's clock.</param>
    public RequestPasswordResetCommandHandler(
        IdentityDbContext dbContext,
        IMessageBus bus,
        IdentityAuditJournal journal,
        IStringLocalizer<EmailTemplates> templates,
        IOptions<PasswordResetOptions> options,
        IClock clock)
    {
        _dbContext = dbContext;
        _bus = bus;
        _journal = journal;
        _templates = templates;
        _options = options.Value;
        _clock = clock;
    }

    /// <summary>Issues a reset token when the address belongs to somebody, and answers either way.</summary>
    /// <param name="command">The address, with the caller's address and client.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// Always success. "We have sent a link if that address belongs to an account" is the only honest
    /// answer this endpoint can give, and it is the same sentence for everybody.
    /// </returns>
    public async Task<Result<bool>> HandleAsync(
        RequestPasswordResetCommand command,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        // Generated before the lookup is branched on, so both paths pay for the CSPRNG draw and the
        // digest. See the type's remarks (2).
        var token = PasswordResetTokenHasher.Generate();
        var tokenHash = PasswordResetTokenHasher.Hash(token);

        var user = await _dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.Email == command.Email, cancellationToken);

        if (user is not null)
        {
            await RetireOutstandingAsync(user.Id, now, cancellationToken);
            _dbContext.PasswordResetTokens.Add(new PasswordResetToken(Guid.NewGuid(), user.Id, tokenHash, now));

            // Saved here rather than left for the journal write below to flush. The audit writer
            // happens to call SaveChanges on the same context today, and a handler that depended on
            // that would stop storing its token the day the journal moved — silently, with the mail
            // still going out and the link in it matching nothing.
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // Written for every request, including one naming an address nobody holds. The journal is
        // the ONLY place a sweep through guessed addresses is visible, precisely because the response
        // is deliberately uninformative — and it is read by administrators, not by the caller, so
        // recording which requests matched a user leaks nothing to the person making them.
        await _journal.RecordClaimAsync(
            user?.Id,
            command.Email,
            AuditActions.PasswordResetRequested,
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        if (user is not null)
        {
            // Published, not invoked. The send happens in the Notifications module, after this request
            // has already answered — see the type's remarks (3).
            await _bus.PublishAsync(Compose(user.Email, token));
        }

        return Result<bool>.Ok(true);
    }

    /// <summary>Marks every unspent token of one user as used, so only the newest one can be spent.</summary>
    /// <param name="userId">The user asking for a reset.</param>
    /// <param name="now">The current instant, taken from <see cref="IClock"/>.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Resolves once the retirements are tracked; nothing is written until the caller saves.</returns>
    private async Task RetireOutstandingAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var outstanding = await _dbContext.PasswordResetTokens
            .Where(existing => existing.UserId == userId && existing.UsedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var existing in outstanding)
        {
            existing.Consume(now);
        }
    }

    /// <summary>Renders the reset message, already localized, ready to hand to the panel's mail queue.</summary>
    /// <param name="recipient">The user's own stored address — never the text the caller typed.</param>
    /// <param name="token">The plaintext token. It exists here, in the message, and nowhere else.</param>
    /// <returns>The message to publish.</returns>
    /// <remarks>
    /// The recipient is read off the USER ROW rather than echoed from the request. The two are equal
    /// today because the row was found by that address, but making the stored value the source means
    /// a future case-insensitive or normalising lookup cannot turn this into a way to have the panel
    /// mail a token to an address of the caller's choosing.
    /// </remarks>
    private SendMailRequested Compose(string recipient, string token)
    {
        var instruction = string.IsNullOrWhiteSpace(_options.PanelUrl)
            ? string.Format(CultureInfo.CurrentCulture, _templates["PasswordResetTokenOnly"], token)
            : string.Format(
                CultureInfo.CurrentCulture,
                _templates["PasswordResetLink"],
                $"{_options.PanelUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(token)}");

        var body = string.Format(CultureInfo.CurrentCulture, _templates["PasswordResetBody"], instruction);

        return new SendMailRequested(recipient, _templates["PasswordResetSubject"], body);
    }
}
