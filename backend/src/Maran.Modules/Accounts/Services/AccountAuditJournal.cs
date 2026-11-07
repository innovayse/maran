using System.Diagnostics;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// Writes this module's audit entries, so every Accounts handler records the same shape and no
/// handler has to remember what an entry is made of.
/// </summary>
/// <remarks>
/// This module's operations are the ones nothing else keeps a record of. A deletion removes the
/// system user, the home directory, every database the account owned and every file-transfer login it
/// owned — SFTP and FTPS alike;
/// once it has run, the account row is gone and the journal is the only place the account's name
/// still exists. A suspension is the same in miniature — the customer's sites stop, and the panel
/// afterwards shows only the state, never who moved it there or when.
///
/// It exists chiefly so that FAILURES are journalled as reliably as successes.
/// <see cref="AuditEntry"/> says outright that "failures are the half of the journal worth reading":
/// a deletion that got part-way through its cascade and then refused, a name or a domain rejected as
/// already taken, and a lookup for an account id that does not exist are all events an operator
/// later needs. Written inline, the failure entry is the one every early <c>return</c> walks past.
///
/// Plan management is journalled through the same type. A plan edit reaches every account on the plan
/// and can move kernel-enforced disk quotas, and a deletion removes the plan's row for good, so they
/// are the same kind of operation nothing else keeps a record of; their subject is the plan's stored
/// name (<c>Plan.DisplayNameKey</c>), which reads the same whoever asks and in whatever language.
///
/// Reseller management is journalled through the same type, and so is everything a reseller does to
/// its own accounts and plans: the entry's actor is the reseller's login, and an administrator viewing
/// as that reseller is recorded beside it by the audit writer, not by this type. The subject of a
/// reseller entry is the reseller's NAME and never its envelope's figures or its contact address.
///
/// For an account, the subject is always the account's NAME, never the agent's output and never a path: the agent's
/// text names absolute paths, uids and mounts on the host, the home directory is derived from the
/// name anyway, and the journal is never deleted (rules/security.md item 8).
/// </remarks>
public sealed class AccountAuditJournal
{
    /// <summary>The panel's append-only journal.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>The authenticated principal, recorded as the actor of every entry.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the journal wrapper.</summary>
    /// <param name="auditWriter">The panel's append-only journal.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public AccountAuditJournal(IAuditWriter auditWriter, ICurrentUser currentUser)
    {
        _auditWriter = auditWriter;
        _currentUser = currentUser;
    }

    /// <summary>Records an operation that took effect.</summary>
    /// <param name="action">The action name, one of the Sdk's <c>*AuditActions</c> constants.</param>
    /// <param name="name">The account's name — the subject an operator will search for.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <param name="accountId">
    /// The account the entry is about, so the journal can be read per account. Passed only by an
    /// operation that is certain of it and whose entry must stay attributable; the deletion flow never
    /// passes one, because the account it removes must not live on in the journal under its own id.
    /// </param>
    public async Task RecordSuccessAsync(
        string action,
        string name,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken,
        Guid? accountId = null)
    {
        await WriteAsync(action, name, ipAddress, userAgent, succeeded: true, accountId, cancellationToken);
    }

    /// <summary>Records an operation that was refused, and why in machine terms.</summary>
    /// <param name="action">The action that was attempted.</param>
    /// <param name="subject">
    /// What it was attempted on. The account's name where one is known; otherwise the identifier the
    /// caller supplied, so that a probe for an account the caller may not see still leaves a trace
    /// naming what was probed for.
    /// </param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <param name="accountId">
    /// The account the refused operation was aimed at, when a row was found for it; <c>null</c> when
    /// none exists to name.
    /// </param>
    public async Task RecordFailureAsync(
        string action,
        string subject,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken,
        Guid? accountId = null)
    {
        await WriteAsync(action, subject, ipAddress, userAgent, succeeded: false, accountId, cancellationToken);
    }

    /// <summary>Builds and stores one entry.</summary>
    /// <param name="action">The action name.</param>
    /// <param name="subject">What the action was attempted on.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="succeeded">Whether the operation took effect.</param>
    /// <param name="accountId">The account the entry is about, or <c>null</c> when it names none.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    private async Task WriteAsync(
        string action,
        string subject,
        string ipAddress,
        string userAgent,
        bool succeeded,
        Guid? accountId,
        CancellationToken cancellationToken)
    {
        var (actorId, actorName) = _currentUser.Caller switch
        {
            AdministratorCaller administrator => (administrator.UserId, administrator.Username),
            ResellerCaller reseller => (reseller.UserId, reseller.Username),
            CustomerCaller customer => (customer.UserId, customer.Username),
            AccountlessCustomerCaller accountless => (accountless.UserId, accountless.Username),
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
                succeeded,
                accountId),
            cancellationToken);
    }
}
