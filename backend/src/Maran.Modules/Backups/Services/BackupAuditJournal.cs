using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Services;

/// <summary>
/// Writes this module's audit entries, so every backup handler records the same shape and no handler
/// has to remember what an entry is made of.
/// </summary>
/// <remarks>
/// <para>
/// It exists chiefly so that FAILURES are journalled as reliably as successes: a refused deletion, a
/// run the agent could not finish and a cross-tenant probe are precisely the events an operator
/// later needs. Written inline, the failure entry is the one every early <c>return</c> walks past.
/// </para>
/// <para>
/// <b>The subject is the account's system user name — what a backup acts on, and what an operator
/// searches the journal for.</b> That is <see cref="AuditEntry"/>'s own contract, and it is the
/// subject every other module already records: <c>DatabaseCreated</c> names the database,
/// <c>AccountSuspended</c> names the account. An earlier version of this journal recorded the
/// backup row's identifier instead, reasoning from the redaction concern below to a conclusion
/// about content — and an operator reading the journal met a bare GUID between two names, which is
/// a subject nobody searches by. The redaction concern never required the id: an account name is
/// neither a path nor a secret. Only a producer that can name no account — a probe for a backup
/// that does not exist for this caller — records the identifier probed for instead, so the attempt
/// still leaves a trace naming what was probed.
/// </para>
/// <para>
/// <b>What is still never recorded:</b> not the destination, not a path, not the archive's size,
/// not the names of the databases it holds. This journal is append-only, is never deleted, and is
/// read by the server's operator — so a line here that named the archive's location would be a
/// permanent, searchable index of where every customer's data sits. The same reasoning bans the
/// agent's own text, which can quote a path under the account's home or a database name: what is
/// recorded of a failure is its machine-stable CODE, through the row, and the operator-facing
/// sentence is logged at the agent client's boundary and stops there.
/// </para>
/// <para>
/// Rows written before this subject policy keep the identifiers they were written with: the
/// journal is append-only, and rewriting history would forge the very record the journal exists to
/// keep.
/// </para>
/// </remarks>
public sealed class BackupAuditJournal
{
    /// <summary>
    /// This module's short name, from which its system actor name is built.
    /// </summary>
    /// <remarks>
    /// The name itself is composed by <see cref="SystemAuditEntry.NameFor"/> so that every module
    /// spells an unattended actor the same way; no account can be confused with it because account
    /// names are validated Linux user names and a hyphenated pair is not one.
    /// </remarks>
    public const string ModuleName = "backups";

    /// <summary>The panel's append-only journal.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>The authenticated principal, recorded as the actor of every entry.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the journal wrapper.</summary>
    /// <param name="auditWriter">The panel's append-only journal.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public BackupAuditJournal(IAuditWriter auditWriter, ICurrentUser currentUser)
    {
        _auditWriter = auditWriter;
        _currentUser = currentUser;
    }

    /// <summary>Records an operation that took effect.</summary>
    /// <param name="action">The action name, one of <see cref="AuditActions"/>.</param>
    /// <param name="subject">
    /// The account's system user name — the subject an operator will search for.
    /// </param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    public async Task RecordSuccessAsync(
        string action,
        string subject,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        await WriteAsync(action, subject, ipAddress, userAgent, succeeded: true, cancellationToken);
    }

    /// <summary>Records an operation that was refused or did not finish.</summary>
    /// <param name="action">The action that was attempted.</param>
    /// <param name="subject">
    /// What it was attempted on: the account's system user name where one could be established, or
    /// the identifier the caller supplied where none could — so a probe for another tenant's backup
    /// still leaves a trace naming what was probed for.
    /// </param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    public async Task RecordFailureAsync(
        string action,
        string subject,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        await WriteAsync(action, subject, ipAddress, userAgent, succeeded: false, cancellationToken);
    }

    /// <summary>Records an unattended operation, which has no signed-in caller at all.</summary>
    /// <param name="action">The action name, one of <see cref="AuditActions"/>.</param>
    /// <param name="subject">
    /// The account's system user name — the subject an operator will search for.
    /// </param>
    /// <param name="succeeded">Whether the operation took effect.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <remarks>
    /// <para>
    /// Outside a request there is no <c>HttpContext</c>, so <see cref="ICurrentUser"/> reports
    /// <see cref="Guid.Empty"/> and an empty name — which is exactly what a failed ANONYMOUS request
    /// records. An operator reading the journal could then not tell a nightly backup from someone
    /// probing the API unauthenticated. <see cref="SystemAuditEntry"/> names the panel instead, and
    /// leaves the address and client columns empty because nothing arrived over HTTP.
    /// </para>
    /// <para>
    /// It matters most on the pruning entries: a retention pass destroys a customer's archive with
    /// nobody having asked for it that night, and the line saying so must name the panel as the
    /// thing that did it — and the account it did it to.
    /// </para>
    /// </remarks>
    public async Task RecordScheduledAsync(
        string action,
        string subject,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        await _auditWriter.WriteAsync(
            SystemAuditEntry.Create(ModuleName, action, subject, succeeded),
            cancellationToken);
    }

    /// <summary>Builds and stores one entry.</summary>
    /// <param name="action">The action name.</param>
    /// <param name="subject">What the action was attempted on.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="succeeded">Whether the operation took effect.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    private async Task WriteAsync(
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
}
