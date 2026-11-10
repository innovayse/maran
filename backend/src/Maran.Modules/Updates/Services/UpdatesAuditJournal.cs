using System.Diagnostics;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Updates.Services;

/// <summary>
/// Writes this module's audit entries, so the request an administrator made and the outcome root reports
/// are recorded in one shape (issue #382).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two kinds of entry, two actors.</b> The request is the administrator's own act and is recorded
/// under their name with the address and agent of the request (<see cref="RecordRequestAsync"/>). What
/// happens after — the root update's outcome, the reaper closing a task — has no signed-in caller, so it
/// is recorded as the panel itself (<see cref="RecordSystemAsync"/>), under the actor name every module
/// spells the same way.
/// </para>
/// <para>
/// <b>What a subject may carry.</b> The request's subject is the fixed <c>request=update</c>, because the
/// request carries nothing else. An outcome's subject is the two release names, which
/// <c>UpdateRequestReport</c> already refused to hold unless they were plain. No path, no URL and no tool
/// output ever reaches the journal, which is append-only and never deleted (rules/security.md item 8).
/// </para>
/// </remarks>
public sealed class UpdatesAuditJournal : ISystemAuditJournal
{
    /// <summary>This module's short name, from which its system actor name is built.</summary>
    public const string ModuleName = "updates";

    /// <summary>The fixed subject of the update request entry.</summary>
    private const string RequestSubject = "request=update";

    /// <summary>The fixed subject of the check request entry (issue #516).</summary>
    private const string CheckRequestSubject = "request=check";

    /// <summary>The panel's append-only journal.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>The authenticated principal, recorded as the actor of a request entry.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the journal wrapper.</summary>
    /// <param name="auditWriter">The panel's append-only journal.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public UpdatesAuditJournal(IAuditWriter auditWriter, ICurrentUser currentUser)
    {
        _auditWriter = auditWriter;
        _currentUser = currentUser;
    }

    /// <summary>Records the administrator's request to update the panel.</summary>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="succeeded">Whether the request was handed to the root update service.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    public async Task RecordRequestAsync(
        string ipAddress,
        string userAgent,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        await WriteRequestAsync(
            UpdatesAuditActions.PanelUpdateRequested,
            RequestSubject,
            ipAddress,
            userAgent,
            succeeded,
            cancellationToken);
    }

    /// <summary>Records the administrator's request to check for updates (issue #516).</summary>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="succeeded">Whether the request was handed to the root check service.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    public async Task RecordCheckRequestAsync(
        string ipAddress,
        string userAgent,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        await WriteRequestAsync(
            UpdatesAuditActions.PanelUpdateCheckRequested,
            CheckRequestSubject,
            ipAddress,
            userAgent,
            succeeded,
            cancellationToken);
    }

    /// <summary>Records something the panel did on its own initiative, with no signed-in caller.</summary>
    /// <param name="action">The action name, one of <see cref="UpdatesAuditActions"/>.</param>
    /// <param name="subject">What it concerned, as <c>from=..;to=..</c>.</param>
    /// <param name="succeeded">Whether it took effect.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    public async Task RecordSystemAsync(
        string action,
        string subject,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        await _auditWriter.WriteAsync(
            SystemAuditEntry.Create(ModuleName, action, subject, succeeded),
            cancellationToken);
    }

    /// <summary>Writes a request entry under the signed-in caller's name.</summary>
    /// <param name="action">The action name, one of <see cref="UpdatesAuditActions"/>.</param>
    /// <param name="subject">The fixed subject of the request.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="succeeded">Whether the request was handed to root.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    private async Task WriteRequestAsync(
        string action,
        string subject,
        string ipAddress,
        string userAgent,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        Guid? userId = null;
        var username = string.Empty;
        switch (_currentUser.Caller)
        {
            case PrincipalCaller principal:
                userId = principal.UserId;
                username = principal.Username;
                break;
            case ApiKeyCaller apiKey:
                username = apiKey.ActorName;
                break;
            case AnonymousCaller:
                break;
            default:
                throw new UnreachableException("Caller is sealed to SharedKernel: six cases exist.");
        }

        await _auditWriter.WriteAsync(
            new AuditEntry(
                userId,
                username,
                action,
                subject,
                ipAddress,
                userAgent,
                succeeded),
            cancellationToken);
    }
}
