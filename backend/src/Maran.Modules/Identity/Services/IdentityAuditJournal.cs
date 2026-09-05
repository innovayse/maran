using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Writes this module's audit entries, so every Identity handler records the same shape and no
/// handler has to remember what an entry is made of.
/// </summary>
/// <remarks>
/// <para>
/// <b>This module's redaction policy is different from every other module's, and the difference is
/// the whole reason the seam has to exist here too.</b> Elsewhere the actor is the signed-in
/// principal, because a caller has already proved who they are before the operation runs. Identity
/// is where that proof is produced, so most of its entries are written with no principal at all: a
/// sign-in, a two-factor check and a password reset are all reachable anonymously. The actor
/// therefore cannot come from <see cref="ICurrentUser"/> by default, and a journal that took it
/// from there would record every failed sign-in as having no actor — losing exactly the column an
/// operator reads a credential sweep out of.
/// </para>
/// <para>
/// So the two actor columns carry two different things, and separating them is this journal's main
/// job. <see cref="AuditEntry.ActorUsername"/> is the identity the caller CLAIMED — a login name, an
/// email address typed into "forgot password" — and it is recorded whether or not it matched
/// anything, because the names that matched nothing are the sweep.
/// <see cref="AuditEntry.ActorUserId"/> is the identity the panel VERIFIED, and it is
/// <c>null</c> whenever nothing was verified, which is the contract's own documented meaning for
/// that value. An id is never written for a claim the request did not stand up.
/// </para>
/// <para>
/// <b>No secret is ever an actor, a subject, or anything else in an entry.</b> A password, a TOTP
/// code, a recovery code, a refresh token, a one-time setup token and a password-reset token all
/// reach these handlers, and none of them may be journalled — the journal is append-only, never
/// deleted, and read by the server's operator (rules/security.md item 8). A reset token in
/// particular is a live permission to take over an account, so its digest is banned as well as its
/// text: recognising a token in an operator's screen is enough to use it. Where the caller's only
/// claim WAS a secret, the entry has no claimed name; it never has a redacted one.
/// </para>
/// <para>
/// The panel names itself as the actor only when there is neither a verified id nor a claimed name —
/// a refresh token replayed from a cookie, a reset token matching no row. Unlike the sibling modules'
/// unattended entries, the origin columns stay FILLED there: those events did arrive over HTTP, from
/// an address, and that address is the only thing the entry has to offer.
/// </para>
/// </remarks>
public sealed class IdentityAuditJournal
{
    /// <summary>
    /// This module's short name, from which its actor name is built for a caller who claimed
    /// nothing nameable.
    /// </summary>
    /// <remarks>
    /// <see cref="SystemAuditEntry.NameFor"/> composes the name, so every module spells such an
    /// actor the same way. It is deliberately not a name a person could ever hold: the journal's
    /// actor column answers "who", and naming the panel is a truthful answer that no login can
    /// impersonate.
    /// </remarks>
    public const string ModuleName = "identity";

    /// <summary>The panel's append-only journal.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>
    /// The authenticated principal, consulted only to put a NAME on an id the handler already knows.
    /// </summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the journal wrapper.</summary>
    /// <param name="auditWriter">The panel's append-only journal.</param>
    /// <param name="currentUser">The authenticated principal of the current request, if any.</param>
    public IdentityAuditJournal(IAuditWriter auditWriter, ICurrentUser currentUser)
    {
        _auditWriter = auditWriter;
        _currentUser = currentUser;
    }

    /// <summary>Records an attempt whose caller named the identity they were acting as.</summary>
    /// <param name="actorUserId">The user the claim matched, or <c>null</c> when it matched nobody.</param>
    /// <param name="claimedName">
    /// The name the caller supplied — a login name, or the address typed into "forgot password".
    /// It is the subject too: what a sign-in acts on is the account it names. Never a secret.
    /// </param>
    /// <param name="action">The action name, one of <see cref="AuditActions"/>.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="succeeded">Whether it took effect. A refused sign-in is the entry worth reading.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    public async Task RecordClaimAsync(
        Guid? actorUserId,
        string claimedName,
        string action,
        string ipAddress,
        string userAgent,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        await WriteAsync(
            actorUserId,
            claimedName,
            action,
            claimedName,
            ipAddress,
            userAgent,
            succeeded,
            cancellationToken);
    }

    /// <summary>Records an operation the panel performed for a user it had already identified.</summary>
    /// <param name="actorUserId">The user, established by the handler from a session or a token.</param>
    /// <param name="action">The action name, one of <see cref="AuditActions"/>.</param>
    /// <param name="subject">
    /// What was acted on — a session id, the user's own id. Never a token and never its digest.
    /// </param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="succeeded">Whether it took effect.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    /// <remarks>
    /// The name is taken from <see cref="ICurrentUser"/> ONLY when the principal is that same user.
    /// Half of these operations are reachable with a cookie and no access token, so there is often
    /// no principal to ask; and where there is one, it can name somebody other than the account the
    /// handler resolved. Writing that name would put a different person in the "who" column, so the
    /// comparison is the condition of using it at all — an entry with a blank name is a gap in the
    /// journal, but an entry with the WRONG name is a false accusation kept forever.
    /// </remarks>
    public async Task RecordIdentifiedAsync(
        Guid actorUserId,
        string action,
        string subject,
        string ipAddress,
        string userAgent,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        var name = _currentUser.UserId == actorUserId ? _currentUser.Username : string.Empty;

        await WriteAsync(actorUserId, name, action, subject, ipAddress, userAgent, succeeded, cancellationToken);
    }

    /// <summary>Records a refused attempt whose caller neither proved nor claimed an identity.</summary>
    /// <param name="action">The action name, one of <see cref="AuditActions"/>.</param>
    /// <param name="subject">What was acted on, where anything is known; otherwise empty.</param>
    /// <param name="ipAddress">The caller's address — the one thing such an entry does carry.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    /// <remarks>
    /// The caller presented a bare secret — a replayed refresh token, a reset token matching no row —
    /// so there is no name to record and nothing was verified. The panel names itself rather than
    /// leaving the column blank, because a blank actor reads as a bug in the writer, and a redacted
    /// stand-in for the secret would be the secret. These attempts are always refusals; a bearer
    /// secret that worked identifies its user, and that entry is
    /// <see cref="RecordIdentifiedAsync"/>'s.
    /// </remarks>
    public async Task RecordUnidentifiedAsync(
        string action,
        string subject,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        await WriteAsync(
            null,
            SystemAuditEntry.NameFor(ModuleName),
            action,
            subject,
            ipAddress,
            userAgent,
            succeeded: false,
            cancellationToken);
    }

    /// <summary>Builds and stores one entry.</summary>
    /// <param name="actorUserId">The verified user, or <c>null</c>.</param>
    /// <param name="actorUsername">The name to record in the actor column.</param>
    /// <param name="action">The action name.</param>
    /// <param name="subject">What the action was attempted on.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="succeeded">Whether the operation took effect.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
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
            new AuditEntry(
                actorUserId,
                actorUsername,
                action,
                subject,
                ipAddress,
                userAgent,
                succeeded),
            cancellationToken);
    }
}
