using System.Diagnostics;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Sftp.Services;

/// <summary>
/// Writes this module's audit entries, so every Sftp handler records the same shape and no handler
/// has to remember what an entry is made of.
/// </summary>
/// <remarks>
/// It exists chiefly so that FAILURES are journalled as reliably as successes.
/// <see cref="AuditEntry"/> says outright that "failures are the half of the journal worth reading",
/// and a refused provisioning, a plan limit hit or a cross-tenant probe are precisely the events an
/// operator later needs. Written inline, the failure entry is the one every early <c>return</c>
/// walks past.
///
/// The subject is always the login's name, never the agent's output: that text may name absolute
/// paths and sockets on the host, and the journal is never deleted (rules/security.md). It is never
/// a password either — an entry naming which credential was reset is the journal keeping the copy
/// this module takes such trouble not to keep.
/// </remarks>
public sealed class SftpAuditJournal
{
    /// <summary>The panel's append-only journal.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>The authenticated principal, recorded as the actor of every entry.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the journal wrapper.</summary>
    /// <param name="auditWriter">The panel's append-only journal.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public SftpAuditJournal(IAuditWriter auditWriter, ICurrentUser currentUser)
    {
        _auditWriter = auditWriter;
        _currentUser = currentUser;
    }

    /// <summary>Records an operation that took effect.</summary>
    /// <param name="action">The action name, one of the Sdk's <c>*AuditActions</c> constants.</param>
    /// <param name="name">The login's name — the subject an operator will search for.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    public async Task RecordSuccessAsync(
        string action,
        string name,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        await WriteAsync(action, name, ipAddress, userAgent, succeeded: true, cancellationToken);
    }

    /// <summary>Records an operation that was refused, and why in machine terms.</summary>
    /// <param name="action">The action that was attempted.</param>
    /// <param name="subject">
    /// What it was attempted on. The login's name where one is known; otherwise the identifier the
    /// caller supplied, so that a probe for a login the caller may not see still leaves a trace
    /// naming what was probed for.
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

    /// <summary>Records a key change that took effect.</summary>
    /// <param name="action">The action name, <c>SftpKeyAdded</c> or <c>SftpKeyRemoved</c>.</param>
    /// <param name="loginName">The login's name.</param>
    /// <param name="label">The key's label, as stored.</param>
    /// <param name="fingerprint">The key's fingerprint.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    public async Task RecordKeySuccessAsync(
        string action,
        string loginName,
        string label,
        string fingerprint,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        var subject = KeySubject(loginName, label, fingerprint);

        await WriteAsync(action, subject, ipAddress, userAgent, succeeded: true, cancellationToken);
    }

    /// <summary>Records a key change that was refused or did not complete.</summary>
    /// <param name="action">The action that was attempted.</param>
    /// <param name="loginName">The login's name, or the supplied identifier when no row was found.</param>
    /// <param name="label">
    /// The key's label as STORED, or empty when the failure came before a label was held — a label the
    /// caller typed and nothing has accepted yet is not written into a journal that is never deleted.
    /// </param>
    /// <param name="fingerprint">The key's fingerprint, or empty when the key was never parsed.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    public async Task RecordKeyFailureAsync(
        string action,
        string loginName,
        string label,
        string fingerprint,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        var subject = KeySubject(loginName, label, fingerprint);

        await WriteAsync(action, subject, ipAddress, userAgent, succeeded: false, cancellationToken);
    }

    /// <summary>Composes the subject of a key entry from the three facts it may carry.</summary>
    /// <param name="loginName">The login's name.</param>
    /// <param name="label">The key's label, or empty.</param>
    /// <param name="fingerprint">The key's fingerprint, or empty.</param>
    /// <returns>
    /// The non-empty parts joined with <c>" | "</c>. The key body, the comment of the pasted line and
    /// the agent's text are never parameters of this method, so none of them can reach the journal.
    /// </returns>
    private static string KeySubject(string loginName, string label, string fingerprint)
    {
        return string.Join(
            " | ",
            new[] { loginName, label, fingerprint }.Where(part =>
            {
                return part.Length > 0;
            }));
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
        var (actorId, actorName) = _currentUser.Caller switch
        {
            AdministratorCaller administrator => (administrator.UserId, administrator.Username),
            CustomerCaller customer => (customer.UserId, customer.Username),
            AccountlessCustomerCaller accountless => (accountless.UserId, accountless.Username),
            ResellerCaller reseller => (reseller.UserId, reseller.Username),
            AnonymousCaller => (Guid.Empty, string.Empty),
            _ => throw new UnreachableException("Caller is sealed to SharedKernel: five cases exist."),
        };

        await _auditWriter.WriteAsync(
            new AuditEntry(
                actorId,
                actorName,
                action,
                subject,
                ipAddress,
                userAgent,
                succeeded),
            cancellationToken);
    }
}
