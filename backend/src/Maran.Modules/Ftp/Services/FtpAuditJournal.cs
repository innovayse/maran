using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ftp.Services;

/// <summary>
/// Writes this module's audit entries, so every Ftp handler records the same shape and no handler
/// has to remember what an entry is made of.
/// </summary>
/// <remarks>
/// <para>
/// It exists chiefly so that FAILURES are journalled as reliably as successes.
/// <see cref="AuditEntry"/> says outright that "failures are the half of the journal worth reading",
/// and for this module that half is nearly all of it: a refused enable is what an operator reads
/// when FTPS did not come up, and a refused TLS reload is a daemon still serving superseded
/// certificate material with nothing else on any screen to say so.
/// </para>
/// <para>
/// The subject is the hostname for the server-level actions and the LOGIN'S NAME for the three
/// login actions — never the agent's own text, which may name absolute paths
/// and sockets on the host and which the journal keeps forever (rules/security.md item 8). That text
/// is logged at the boundary that receives it, by <c>AgentErrorTranslator</c>, and nothing here
/// copies it into a row.
/// </para>
/// <para>
/// <b>The six action names below belong in <c>Maran.Sdk/Contracts/AuditActions</c></b>, beside
/// every other action the panel writes, and they are stated here only because this change may not
/// write to the Sdk project. Every call site already goes through this one type, so moving them is a
/// six-line append there and a six-symbol change here.
/// </para>
/// </remarks>
public sealed class FtpAuditJournal
{
    /// <summary>Action name: the server's FTPS daemon was configured and started.</summary>
    public const string FtpsEnabled = "FtpsEnabled";

    /// <summary>Action name: the server's FTPS daemon was stopped and taken out of the boot sequence.</summary>
    public const string FtpsDisabled = "FtpsDisabled";

    /// <summary>Action name: the daemon was restarted to pick up replaced certificate material.</summary>
    public const string FtpsTlsReloaded = "FtpsTlsReloaded";

    /// <summary>Action name: a customer FTPS login was created.</summary>
    public const string FtpUserCreated = "FtpUserCreated";

    /// <summary>Action name: a customer FTPS login was removed.</summary>
    public const string FtpUserDeleted = "FtpUserDeleted";

    /// <summary>Action name: a customer FTPS login was given a new password.</summary>
    public const string FtpUserPasswordReset = "FtpUserPasswordReset";

    /// <summary>The module's short name, which forms the actor name of its unattended entries.</summary>
    private const string ModuleName = "ftp";

    /// <summary>The panel's append-only journal.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>The authenticated principal, recorded as the actor of every attended entry.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the journal wrapper.</summary>
    /// <param name="auditWriter">The panel's append-only journal.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public FtpAuditJournal(IAuditWriter auditWriter, ICurrentUser currentUser)
    {
        _auditWriter = auditWriter;
        _currentUser = currentUser;
    }

    /// <summary>Records a signed-in caller's operation that took effect.</summary>
    /// <param name="action">The action name, one of this type's constants.</param>
    /// <param name="subject">
    /// What the action took effect on — the hostname the daemon was configured for, or the login's
    /// name — which is the value an operator later searches on.
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

    /// <summary>Records a signed-in caller's operation that was refused.</summary>
    /// <param name="action">The action that was attempted.</param>
    /// <param name="subject">
    /// What it was attempted on: the hostname the caller supplied, the login's name, or — when no
    /// row was found — the identifier they supplied, so a probe for a login the caller may not see
    /// still leaves a trace naming what was probed for. That trace is the only place an operator's
    /// typo and an attacker's probe are told apart, because the ANSWER to both is "not found".
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

    /// <summary>Records work the panel did on its own initiative, with no signed-in caller.</summary>
    /// <param name="action">The action name, one of this type's constants.</param>
    /// <param name="hostname">The hostname acted on.</param>
    /// <param name="succeeded">Whether it took effect.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <remarks>
    /// The reload driven by a certificate installation has no caller: it happens because the Ssl
    /// module replaced material, possibly on a renewal timer at four in the morning. The actor is
    /// therefore <c>maran-ftp</c> and the address and client columns are empty, which is
    /// <see cref="SystemAuditEntry"/>'s one spelling of "nobody signed in did this" — writing the
    /// actor's name into the address column instead makes the journal answer "where did this come
    /// from" with a lie.
    /// </remarks>
    public async Task RecordUnattendedAsync(
        string action,
        string hostname,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        await _auditWriter.WriteAsync(
            SystemAuditEntry.Create(ModuleName, action, hostname, succeeded),
            cancellationToken);
    }

    /// <summary>Builds and stores one attended entry.</summary>
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
