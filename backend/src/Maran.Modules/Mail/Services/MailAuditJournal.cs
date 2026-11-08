using System.Diagnostics;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Mail.Services;

/// <summary>
/// Writes this module's audit entries, so every Mail handler records the same shape and no handler has to
/// remember what an entry is made of (rules/csharp.md: a module writes its entries only through its own journal).
/// </summary>
/// <remarks>
/// <para>
/// It exists chiefly so that FAILURES are journalled as reliably as successes: a refused enable is what an
/// operator reads when mail did not come up, and a refused mailbox creation is what a customer's support
/// request is about. The action names are <see cref="HostedMailAuditActions"/>'s, shared with the rest of the
/// panel's journal vocabulary and distinct from <see cref="OutgoingMailAuditActions"/>, which names the panel's own
/// outgoing mail.
/// </para>
/// <para>
/// The subject is the host name for server-level actions, the domain name or full address for the rest,
/// and the identifier the caller supplied when no row was found — never the agent's own text, which may
/// name absolute paths on the host and which the journal keeps forever (rules/security.md item 8), and
/// never a password, a hash or a key.
/// </para>
/// </remarks>
public sealed class MailAuditJournal
{
    /// <summary>The module's short name, which forms the actor name of its unattended entries.</summary>
    private const string ModuleName = "mail";

    /// <summary>The panel's append-only journal.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>The authenticated principal, recorded as the actor of every attended entry.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the journal wrapper.</summary>
    /// <param name="auditWriter">The panel's append-only journal.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public MailAuditJournal(IAuditWriter auditWriter, ICurrentUser currentUser)
    {
        _auditWriter = auditWriter;
        _currentUser = currentUser;
    }

    /// <summary>Records a signed-in caller's operation that took effect.</summary>
    /// <param name="action">The action name, one of <see cref="HostedMailAuditActions"/>.</param>
    /// <param name="subject">
    /// What the action took effect on — the host name the daemon was configured for, or the domain, address or alias
    /// the action concerned — which is the value an operator later searches on.
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
    /// What it was attempted on: the hostname the caller supplied, the address or domain, or — when no
    /// row was found — the identifier they supplied, so a probe for a resource the caller may not see
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
    /// <param name="action">The action name, one of <see cref="HostedMailAuditActions"/>.</param>
    /// <param name="hostname">The hostname acted on.</param>
    /// <param name="succeeded">Whether it took effect.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <remarks>
    /// The reload driven by a certificate installation has no caller: it happens because the Ssl
    /// module replaced material, possibly on a renewal timer at four in the morning. The actor is
    /// therefore <c>maran-mail</c> and the address and client columns are empty, which is
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
        var (actorId, actorName) = _currentUser.Caller switch
        {
            AdministratorCaller administrator => (administrator.UserId, administrator.Username),
            CustomerCaller customer => (customer.UserId, customer.Username),
            AccountlessCustomerCaller accountless => (accountless.UserId, accountless.Username),
            ResellerCaller reseller => (reseller.UserId, reseller.Username),
            ApiKeyCaller apiKey => (Guid.Empty, apiKey.ActorName),
            AnonymousCaller => (Guid.Empty, string.Empty),
            _ => throw new UnreachableException("Caller is sealed to SharedKernel: six cases exist."),
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
