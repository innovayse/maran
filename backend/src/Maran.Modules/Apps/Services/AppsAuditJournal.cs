using System.Diagnostics;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Apps.Services;

/// <summary>
/// Writes this module's audit entries, so the request an administrator made and the outcome the job
/// reaches are recorded in one shape (issue #517).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two kinds of entry, two actors.</b> A request, and its refusal, is the administrator's own act and is
/// recorded under their name with the address and agent of the request. What happens after the 202, the
/// install ending or failing, has no signed-in caller, so it is recorded as the panel itself
/// (<see cref="RecordSystemAsync"/>), under the actor name every module spells the same way.
/// </para>
/// <para>
/// <b>What a subject may carry.</b> The subject is the app id (and, for a service action, the action): members
/// of closed sets, never text the caller typed and never the agent's own output, which can name paths on the host and which the
/// append-only journal would keep for ever (rules/security.md item 8).
/// </para>
/// </remarks>
public sealed class AppsAuditJournal : ISystemAuditJournal
{
    /// <summary>This module's short name, from which its system actor name is built.</summary>
    public const string ModuleName = "apps";

    /// <summary>An administrator asked for an app to be installed; failed when the request was refused.</summary>
    public const string AppInstallRequested = "AppInstallRequested";

    /// <summary>An administrator asked for an app to be removed; failed when the request was refused.</summary>
    public const string AppUninstallRequested = "AppUninstallRequested";

    /// <summary>An install ended: succeeded when the app is installed, failed when it is not or its outcome is unknown.</summary>
    public const string AppInstalled = "AppInstalled";

    /// <summary>A removal ended: succeeded when the app is gone, failed when it is not or its outcome is unknown.</summary>
    public const string AppUninstalled = "AppUninstalled";

    /// <summary>An administrator started, stopped, restarted or reloaded an app's service.</summary>
    public const string AppServiceControlled = "AppServiceControlled";

    /// <summary>The panel's append-only journal.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>The authenticated principal, recorded as the actor of a request entry.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the journal wrapper.</summary>
    /// <param name="auditWriter">The panel's append-only journal.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public AppsAuditJournal(IAuditWriter auditWriter, ICurrentUser currentUser)
    {
        _auditWriter = auditWriter;
        _currentUser = currentUser;
    }

    /// <summary>Records an operation an administrator asked for.</summary>
    /// <param name="action">The action name, one of the constants of this type.</param>
    /// <param name="subject">The app it concerned (a service action adds <c>;action=&lt;action&gt;</c>).</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="succeeded">Whether the request was accepted (or, for a service action, took effect).</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    public async Task RecordRequestAsync(
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
            new AuditEntry(userId, username, action, subject, ipAddress, userAgent, succeeded),
            cancellationToken);
    }

    /// <summary>Records something the panel did on its own initiative, with no signed-in caller.</summary>
    /// <param name="action">The action name, one of the constants of this type.</param>
    /// <param name="subject">The app it concerned.</param>
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
}
