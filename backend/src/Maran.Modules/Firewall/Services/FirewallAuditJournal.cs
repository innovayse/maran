using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Firewall.Services;

/// <summary>
/// Writes this module's audit entries, so every Firewall handler records the same shape and no
/// handler has to remember what an entry is made of.
/// </summary>
/// <remarks>
/// <para>
/// It exists chiefly so that FAILURES and NON-EVENTS are journalled as reliably as successes. "Who
/// banned this customer's office" and "why was this address NOT banned" are the two questions an
/// operator actually arrives with, and both are answered by entries that an inline write is the one
/// every early <c>return</c> walks past.
/// </para>
/// <para>
/// The actor may be nobody. A brute-force ban is placed by the panel itself, with no signed-in user
/// anywhere: <see cref="RecordSystemAsync"/> records it under the module's own name so the journal
/// distinguishes a ban an administrator asked for from one the detector placed — an automatic ban
/// with no entry is indistinguishable from a network fault.
/// </para>
/// <para>
/// The subject is always the address or the rule, never the agent's own output: that text may name
/// paths and tool versions on the host, and the journal is never deleted (rules/security.md).
/// </para>
/// </remarks>
public sealed class FirewallAuditJournal
{
    /// <summary>
    /// The actor name recorded for work the panel does on its own initiative.
    /// </summary>
    /// <remarks>
    /// Not a user name, and deliberately not one a person could ever hold: the journal's actor column
    /// answers "who", and "the brute-force detector" is a truthful answer that no login can
    /// impersonate.
    /// </remarks>
    public const string SystemActor = "maran-firewall";

    /// <summary>The panel's append-only journal.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>The authenticated principal, recorded as the actor of a request-driven entry.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the journal wrapper.</summary>
    /// <param name="auditWriter">The panel's append-only journal.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public FirewallAuditJournal(IAuditWriter auditWriter, ICurrentUser currentUser)
    {
        _auditWriter = auditWriter;
        _currentUser = currentUser;
    }

    /// <summary>Records an operation an administrator asked for and which took effect.</summary>
    /// <param name="action">The action name, one of <see cref="AuditActions"/>.</param>
    /// <param name="subject">What it was done to — an address, or a rule in "tcp/8080 from 0.0.0.0/0" form.</param>
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
        await WriteAsync(
            _currentUser.UserId,
            _currentUser.Username,
            action,
            subject,
            ipAddress,
            userAgent,
            succeeded: true,
            cancellationToken);
    }

    /// <summary>Records an operation an administrator asked for and which was refused.</summary>
    /// <param name="action">The action that was attempted.</param>
    /// <param name="subject">What it was attempted on, so the attempt is searchable even when it changed nothing.</param>
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
        await WriteAsync(
            _currentUser.UserId,
            _currentUser.Username,
            action,
            subject,
            ipAddress,
            userAgent,
            succeeded: false,
            cancellationToken);
    }

    /// <summary>Records something the panel did on its own initiative, with no signed-in caller.</summary>
    /// <param name="action">The action name, one of <see cref="AuditActions"/>.</param>
    /// <param name="subject">The address the panel acted on.</param>
    /// <param name="succeeded">Whether it took effect.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <remarks>
    /// The entry's address column carries the SUBJECT's address rather than a caller's, because there
    /// was no caller: the request that triggered this arrived at a different module, minutes ago, and
    /// the only address that means anything here is the one being banned.
    /// </remarks>
    public async Task RecordSystemAsync(
        string action,
        string subject,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        await WriteAsync(null, SystemActor, action, subject, subject, string.Empty, succeeded, cancellationToken);
    }

    /// <summary>Builds and stores one entry.</summary>
    /// <param name="actorUserId">The signed-in user, or null when the panel acted on its own.</param>
    /// <param name="actorUsername">The name the actor is recorded under.</param>
    /// <param name="action">The action name.</param>
    /// <param name="subject">What the action was attempted on.</param>
    /// <param name="ipAddress">The address the entry records as the origin.</param>
    /// <param name="userAgent">The caller's user agent, empty when there was no caller.</param>
    /// <param name="succeeded">Whether the operation took effect.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    private async Task WriteAsync(
        Guid? actorUserId,
        string actorUsername,
        string action,
        string subject,
        string ipAddress,
        string userAgent,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        await _auditWriter.WriteAsync(
            new AuditEntry(actorUserId, actorUsername, action, subject, ipAddress, userAgent, succeeded),
            cancellationToken);
    }
}
