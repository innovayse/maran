using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Notifications.Services;

/// <summary>
/// Writes this module's audit entries, so every Notifications handler records the same shape and no
/// handler has to remember what an entry is made of.
/// </summary>
/// <remarks>
/// <para>
/// <b>Most of what this module journals happened with nobody signed in.</b> A password-reset mail is
/// sent by a background handler minutes after the request that asked for it returned, and an alert
/// mail is asked for by a sampler on a timer. So <see cref="RecordSystemAsync"/> is the method this
/// module uses most, and it records the panel's own name as the actor — an automatic action with no
/// entry is indistinguishable from one that never happened.
/// </para>
/// <para>
/// <b>The subject is never the mail's content and never the recipient's credential.</b> A mail entry
/// names the recipient address and the reason, because "why did this reset never arrive" is the
/// question the journal exists to answer; it never carries the body, which for a reset mail contains
/// a live token, nor the mail server's own error text, which can name hosts and versions. The
/// journal is append-only and never deleted (rules/security.md item 8).
/// </para>
/// </remarks>
public sealed class NotificationsAuditJournal
{
    /// <summary>
    /// This module's short name, from which its system actor name is built.
    /// </summary>
    /// <remarks>
    /// <see cref="SystemAuditEntry.NameFor"/> composes the name, so every module spells an
    /// unattended actor the same way. It is deliberately not a name a person could ever hold: the
    /// journal's actor column answers "who", and "the panel's notifier" is a truthful answer that no
    /// login can impersonate.
    /// </remarks>
    public const string ModuleName = "notifications";

    /// <summary>The panel's append-only journal.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>The authenticated principal, recorded as the actor of a request-driven entry.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the journal wrapper.</summary>
    /// <param name="auditWriter">The panel's append-only journal.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public NotificationsAuditJournal(IAuditWriter auditWriter, ICurrentUser currentUser)
    {
        _auditWriter = auditWriter;
        _currentUser = currentUser;
    }

    /// <summary>Records an operation an administrator asked for.</summary>
    /// <param name="action">The action name, one of <see cref="AuditActions"/>.</param>
    /// <param name="subject">What it was done to — the mail server's host, a recipient address.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="succeeded">Whether it took effect.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    public async Task RecordRequestAsync(
        string action,
        string subject,
        string ipAddress,
        string userAgent,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        await _auditWriter.WriteAsync(
            new AuditEntry(
                _currentUser.UserId,
                _currentUser.Username,
                action,
                subject,
                ipAddress,
                userAgent,
                succeeded),
            cancellationToken);
    }

    /// <summary>Records something the panel did on its own initiative, with no signed-in caller.</summary>
    /// <param name="action">The action name, one of <see cref="AuditActions"/>.</param>
    /// <param name="subject">What the panel acted on — a recipient address.</param>
    /// <param name="succeeded">Whether it took effect.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <remarks>
    /// The address and client columns are left EMPTY rather than filled with the panel's own name,
    /// because there was no caller: the mail sender runs off a queue, so any origin written here
    /// would be a fiction the journal cannot be read back out of.
    /// </remarks>
    public async Task RecordSystemAsync(
        string action,
        string subject,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        await _auditWriter.WriteAsync(
            SystemAuditEntry.Create(ModuleName, action, subject, succeeded),
            cancellationToken);
    }
}
