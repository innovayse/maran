using System.Diagnostics;
using Maran.Modules.Files.Models;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Files.Services;

/// <summary>
/// Writes this module's audit entries, so every Files handler records the same shape and no handler
/// has to remember what an entry is made of.
/// </summary>
/// <remarks>
/// <para>
/// It exists chiefly so that FAILURES are journalled as reliably as successes: a cross-tenant probe, a
/// refused deletion and a suspended account are precisely the events an operator later needs, and
/// written inline the failure entry is the one every early <c>return</c> walks past.
/// </para>
/// <para>
/// <b>The subject is the account's name and the validated relative path, and it is NEVER file
/// content.</b> The journal is append-only and never deleted (rules/security.md), and it is read by the
/// server's operator. A path is the customer's own text and is kept because an operator cannot
/// investigate a deletion without it; a file's content, a text a customer saved, or the agent's own
/// sentence — which can quote a path outside the customer's view — is not recorded anywhere here. For a
/// probe the account does not exist for the caller, so the subject is the identifier that was probed.
/// </para>
/// <para>
/// A subject is cleaned of control characters and capped here as well as by the path rule: a refusal is
/// written for values the rule never saw.
/// </para>
/// </remarks>
public sealed class FilesAuditJournal : ISystemAuditJournal
{
    /// <summary>The module name a system entry is attributed to; the actor reads <c>maran-files</c>.</summary>
    public const string ModuleName = "files";

    /// <summary>The most characters of a subject kept; the journal column holds 256.</summary>
    private const int SubjectMaxCharacters = 256;

    /// <summary>The panel's append-only journal.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>The authenticated principal, recorded as the actor of every entry.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the journal wrapper.</summary>
    /// <param name="auditWriter">The panel's append-only journal.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public FilesAuditJournal(IAuditWriter auditWriter, ICurrentUser currentUser)
    {
        _auditWriter = auditWriter;
        _currentUser = currentUser;
    }

    /// <summary>Builds the subject of an entry about one path of one account.</summary>
    /// <param name="accountName">The account's system user name, read from the directory.</param>
    /// <param name="path">The validated relative path; empty for the home.</param>
    /// <returns>The subject, <c>name:path</c>.</returns>
    public static string SubjectOf(string accountName, string path)
    {
        return $"{accountName}:{path}";
    }

    /// <summary>Records an operation that took effect.</summary>
    /// <param name="action">One of the Sdk's <see cref="FileAuditActions"/> constants.</param>
    /// <param name="accountId">The account the operation was on, when it is known to exist.</param>
    /// <param name="subject">What it was on; see <see cref="SubjectOf"/>. Never content.</param>
    /// <param name="origin">The caller's address and user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    public async Task RecordSuccessAsync(
        string action,
        Guid? accountId,
        string subject,
        FilesCallOrigin origin,
        CancellationToken cancellationToken)
    {
        await WriteAsync(action, accountId, subject, origin, succeeded: true, cancellationToken);
    }

    /// <summary>Records an operation that was refused, and on what.</summary>
    /// <param name="action">The action that was attempted.</param>
    /// <param name="accountId">The account, when it is known to exist; null for a probe of an id the caller may not see.</param>
    /// <param name="subject">
    /// What it was attempted on — for a probe, the identifier that was probed, so the trace names what
    /// was looked for.
    /// </param>
    /// <param name="origin">The caller's address and user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    public async Task RecordFailureAsync(
        string action,
        Guid? accountId,
        string subject,
        FilesCallOrigin origin,
        CancellationToken cancellationToken)
    {
        await WriteAsync(action, accountId, subject, origin, succeeded: false, cancellationToken);
    }

    /// <summary>Records work the panel did on its own, with no caller behind it: the trash retention.</summary>
    /// <param name="action">One of the Sdk's <see cref="FileAuditActions"/> constants.</param>
    /// <param name="subject">What it was on; see <see cref="SubjectOf"/>. Never content.</param>
    /// <param name="succeeded">Whether the operation took effect.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <remarks>
    /// The actor is the panel and not the empty name a failed anonymous request records, so an operator
    /// can tell the night's retention from someone probing the API. <see cref="SystemAuditEntry"/> owns
    /// that spelling, the same one every module's unattended work uses.
    /// </remarks>
    public async Task RecordSystemAsync(
        string action,
        string subject,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        await _auditWriter.WriteAsync(
            SystemAuditEntry.Create(ModuleName, action, NotificationText.Clean(subject, SubjectMaxCharacters), succeeded),
            cancellationToken);
    }

    /// <summary>Builds and stores one entry.</summary>
    /// <param name="action">The action name.</param>
    /// <param name="accountId">The account, when known.</param>
    /// <param name="subject">What the action was attempted on.</param>
    /// <param name="origin">The caller's address and user agent.</param>
    /// <param name="succeeded">Whether the operation took effect.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    private async Task WriteAsync(
        string action,
        Guid? accountId,
        string subject,
        FilesCallOrigin origin,
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
                NotificationText.Clean(subject, SubjectMaxCharacters),
                origin.IpAddress,
                origin.UserAgent,
                succeeded,
                accountId),
            cancellationToken);
    }
}
