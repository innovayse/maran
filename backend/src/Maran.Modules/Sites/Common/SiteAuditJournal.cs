using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.Common;

/// <summary>
/// Writes this module's audit entries, so every Sites handler records the same shape and no handler
/// has to remember what an entry is made of.
/// </summary>
/// <remarks>
/// It exists chiefly so that FAILURES are journalled as reliably as successes.
/// <see cref="AuditEntry"/> says outright that "failures are the half of the journal worth reading",
/// and a refused provisioning, a plan limit hit or a cross-tenant probe are precisely the events an
/// operator later needs. Written inline, the failure entry is the one every early <c>return</c>
/// walks past — which is what happened here before this type existed: all five handlers journalled
/// only their success path.
///
/// The subject is always the site's domain, never the agent's output: that text may name absolute
/// paths on the host, and the journal is never deleted (rules/security.md).
/// </remarks>
public sealed class SiteAuditJournal
{
    /// <summary>The panel's append-only journal.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>The authenticated principal, recorded as the actor of every entry.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the journal wrapper.</summary>
    /// <param name="auditWriter">The panel's append-only journal.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public SiteAuditJournal(IAuditWriter auditWriter, ICurrentUser currentUser)
    {
        _auditWriter = auditWriter;
        _currentUser = currentUser;
    }

    /// <summary>Records an operation that took effect.</summary>
    /// <param name="action">The action name, one of <see cref="AuditActions"/>.</param>
    /// <param name="domain">The site's primary domain — the subject an operator will search for.</param>
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

    /// <summary>Records an operation that was refused, and why in machine terms.</summary>
    /// <param name="action">The action that was attempted.</param>
    /// <param name="subject">
    /// What it was attempted on. The domain where one is known; otherwise the identifier the caller
    /// supplied, so that a probe for a site the caller may not see still leaves a trace naming what
    /// was probed for.
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
