using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Common;

/// <summary>
/// Writes this module's audit entries, so every certificate handler records the same shape and no
/// handler has to remember what an entry is made of.
/// </summary>
/// <remarks>
/// It exists chiefly so that FAILURES are journalled as reliably as successes: a refused issuance, a
/// domain that belongs to somebody else and a rejected order are precisely the events an operator
/// later needs (<see cref="AuditEntry"/>).
///
/// The subject is always the DOMAIN. Never the material, never the authority's text, never a path
/// into the certificate store: the journal is append-only and never deleted, so anything written here
/// is written forever (rules/security.md item 8).
/// </remarks>
public sealed class CertificateAuditJournal
{
    /// <summary>
    /// The actor name every unattended operation is recorded under. A constant, not a fabricated
    /// user: an operator who searches the journal for it finds the scheduled pass and nothing else,
    /// and no real account can be confused with it because no account may be named this — the agent's
    /// account names are validated Linux user names and a hyphenated pair is not one.
    /// </summary>
    public const string SystemActor = "scheduled-renewal";

    /// <summary>The panel's append-only journal.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>The authenticated principal, recorded as the actor of every entry.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the journal wrapper.</summary>
    /// <param name="auditWriter">The panel's append-only journal.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public CertificateAuditJournal(IAuditWriter auditWriter, ICurrentUser currentUser)
    {
        _auditWriter = auditWriter;
        _currentUser = currentUser;
    }

    /// <summary>Records an operation that took effect.</summary>
    /// <param name="action">The action name, one of <see cref="AuditActions"/>.</param>
    /// <param name="domain">The certificate's domain — the subject an operator will search for.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    public async Task RecordSuccessAsync(
        string action,
        string domain,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        await WriteAsync(action, domain, ipAddress, userAgent, succeeded: true, cancellationToken);
    }

    /// <summary>Records an operation that was refused.</summary>
    /// <param name="action">The action that was attempted.</param>
    /// <param name="subject">
    /// What it was attempted on. The domain where one is known; otherwise the identifier the caller
    /// supplied, so that a probe for a certificate the caller may not see still leaves a trace naming
    /// what was probed for.
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
    /// <param name="domain">The certificate's domain.</param>
    /// <param name="succeeded">Whether the operation took effect.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <remarks>
    /// Outside a request there is no <c>HttpContext</c>, so <see cref="ICurrentUser"/> reports
    /// <see cref="Guid.Empty"/> and an empty name — which is exactly what a failed ANONYMOUS request
    /// records. An operator reading the journal could then not tell a nightly renewal from someone
    /// probing the API unauthenticated. This writes <see cref="SystemActor"/> instead, so the pass
    /// names itself without inventing a user that does not exist.
    /// </remarks>
    public async Task RecordScheduledAsync(
        string action,
        string domain,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        await _auditWriter.WriteAsync(
            new AuditEntry(Guid.Empty, SystemActor, action, domain, SystemActor, SystemActor, succeeded),
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
