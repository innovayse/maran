using Maran.Modules.Cron.Common;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Cron.Services;

/// <summary>
/// Writes this module's audit entries, so every Cron handler records the same shape and no handler
/// has to remember what an entry is made of.
/// </summary>
/// <remarks>
/// <para>
/// It exists chiefly so that FAILURES are journalled as reliably as successes.
/// <see cref="AuditEntry"/> says outright that "failures are the half of the journal worth reading",
/// and a refused installation, a plan limit hit or a cross-tenant probe are precisely the events an
/// operator later needs. Written inline, the failure entry is the one every early <c>return</c>
/// walks past.
/// </para>
/// <para>
/// <b>The subject is an IDENTIFIER — the entry id, or the account id when no entry exists yet — and
/// it is NEVER the command.</b> A cron command is the customer's own text and can legitimately carry
/// a credential: <c>mysql -pSECRET</c>, a <c>curl</c> with a token in the URL. This journal is
/// append-only and is never deleted (rules/security.md), and it is read by the server's operator. An
/// id is enough to find the entry the operator is asking about; a command recorded here is a
/// customer's password kept forever, in a place they did not put it.
/// </para>
/// <para>
/// The same reasoning bans the agent's own text, which can name absolute paths under the account's
/// home, and the environment VALUES — an environment change records the names that were set and
/// nothing else, because <c>DATABASE_URL</c> is a useful thing for an operator to know changed and
/// its value is a credential.
/// </para>
/// <para>
/// None of this applies to what the panel shows the customer. The command and the values go back to
/// their owner in full (<see cref="CronEntryDto.Command"/>) — they are sensitive from the operator's
/// journal, not from the person who wrote them.
/// </para>
/// </remarks>
public sealed class CronAuditJournal
{
    /// <summary>The panel's append-only journal.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>The authenticated principal, recorded as the actor of every entry.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the journal wrapper.</summary>
    /// <param name="auditWriter">The panel's append-only journal.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public CronAuditJournal(IAuditWriter auditWriter, ICurrentUser currentUser)
    {
        _auditWriter = auditWriter;
        _currentUser = currentUser;
    }

    /// <summary>Records an operation that took effect.</summary>
    /// <param name="action">The action name, one of <see cref="AuditActions"/>.</param>
    /// <param name="subject">
    /// The entry's identifier — never its command. For an environment change, the names that were
    /// set; never their values.
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

    /// <summary>Records an operation that was refused, and why in machine terms.</summary>
    /// <param name="action">The action that was attempted.</param>
    /// <param name="subject">
    /// What it was attempted on. The entry's identifier where one is known — including one the
    /// caller supplied for an entry they may not see, so that a probe still leaves a trace naming
    /// what was probed for — and otherwise the account's identifier, which is what a creation is
    /// attempted against before any entry exists.
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
