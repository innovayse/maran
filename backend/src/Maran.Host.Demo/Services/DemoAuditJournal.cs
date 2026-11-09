using System.Diagnostics;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;

namespace Maran.Host.Demo.Services;

/// <summary>
/// Writes the audit entries of the demo guard, so every one has the same shape and nothing else in the
/// demo builds an entry (rules/csharp.md "A module writes its audit entries through its own journal").
/// </summary>
/// <remarks>
/// An entry names the refused action by its id and the area that explains it, never a route value: a
/// route value can be a name or an id a visitor typed, and the demo's journal is read by other visitors.
/// The address and user agent it stores are the pseudonym and the coarse family the pseudonymising
/// middleware already put on the request (threat note T9), so the journal never holds a real address.
/// </remarks>
public sealed class DemoAuditJournal
{
    /// <summary>Recorded when a mutating call was refused by the table.</summary>
    public const string ActionRefused = "DemoActionRefused";

    /// <summary>Recorded once, when the demo's whole mutation budget ran out.</summary>
    public const string BudgetExhausted = "DemoBudgetExhausted";

    /// <summary>The panel's append-only journal.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>The authenticated principal, recorded as the actor.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the journal wrapper.</summary>
    /// <param name="auditWriter">The panel's append-only journal.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public DemoAuditJournal(IAuditWriter auditWriter, ICurrentUser currentUser)
    {
        _auditWriter = auditWriter;
        _currentUser = currentUser;
    }

    /// <summary>Records a refused call.</summary>
    /// <param name="endpointLabel">The endpoint's label, from <c>DemoWriteGuard.LabelOf</c>.</param>
    /// <param name="areaId">The refusal area's id.</param>
    /// <param name="ipAddress">The caller's pseudonymous address.</param>
    /// <param name="userAgent">The caller's coarse user agent family.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    public async Task RecordRefusalAsync(
        string endpointLabel,
        string areaId,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        await WriteAsync(ActionRefused, $"{endpointLabel} ({areaId})", ipAddress, userAgent, cancellationToken);
    }

    /// <summary>Records that the demo's whole mutation budget ran out.</summary>
    /// <param name="ipAddress">The pseudonymous address of the call that found it empty.</param>
    /// <param name="userAgent">The coarse user agent family of that call.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    public async Task RecordBudgetExhaustedAsync(
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        await WriteAsync(BudgetExhausted, "global mutation budget", ipAddress, userAgent, cancellationToken);
    }

    /// <summary>Builds and stores one entry; a refusal is, by definition, one that did not succeed.</summary>
    /// <param name="action">The action name.</param>
    /// <param name="subject">What it was about.</param>
    /// <param name="ipAddress">The caller's pseudonymous address.</param>
    /// <param name="userAgent">The caller's coarse user agent family.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    private async Task WriteAsync(
        string action,
        string subject,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        var (userId, username) = _currentUser.Caller switch
        {
            PrincipalCaller principal => (principal.UserId, principal.Username),
            ApiKeyCaller apiKey => (Guid.Empty, apiKey.ActorName),
            AnonymousCaller => (Guid.Empty, string.Empty),
            _ => throw new UnreachableException("Caller is sealed to SharedKernel: six cases exist."),
        };

        await _auditWriter.WriteAsync(
            new AuditEntry(userId, username, action, subject, ipAddress, userAgent, false),
            cancellationToken);
    }
}
