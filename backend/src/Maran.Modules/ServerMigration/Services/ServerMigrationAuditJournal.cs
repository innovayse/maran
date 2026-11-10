using System.Diagnostics;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.ServerMigration.Services;

/// <summary>
/// Writes this module's audit entries, so every Migration handler records the same shape and no handler has to
/// remember what an entry is made of.
/// </summary>
/// <remarks>
/// <para>
/// Every entry names the server by its host and its id, or what was attempted, and nothing else. It never carries
/// a key of any kind: not the panel's private key, not the public key line, not a host key, and not the agent's own
/// text, which may name paths and tools on the source and which the journal keeps forever (rules/security.md
/// item 8). The actions are <see cref="MigrationAuditActions"/>.
/// </para>
/// <para>
/// Almost every entry is a request-driven one under the signed-in administrator's name. There are two others, and
/// each is spelled so that it cannot be taken for a person: the pull of a package by another panel, whose actor is
/// the grant (<see cref="RecordPullAsync"/>), and the expiry of an export, whose actor is the panel itself
/// (<see cref="RecordSystemAsync"/>).
/// </para>
/// </remarks>
public sealed class ServerMigrationAuditJournal : ISystemAuditJournal
{
    /// <summary>This module's short name, from which its system actor name is built (<see cref="SystemAuditEntry.NameFor"/>).</summary>
    public const string ModuleName = "servermigration";

    /// <summary>The prefix of the actor of a pull, which tells a grant apart from a login name.</summary>
    private const string GrantActorPrefix = "export-grant:";

    /// <summary>The panel's append-only journal.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>The authenticated principal, recorded as the actor of every entry.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the journal wrapper.</summary>
    /// <param name="auditWriter">The panel's append-only journal.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public ServerMigrationAuditJournal(IAuditWriter auditWriter, ICurrentUser currentUser)
    {
        _auditWriter = auditWriter;
        _currentUser = currentUser;
    }

    /// <summary>Records a request another panel made with an export grant.</summary>
    /// <param name="grantId">The grant that was presented; the actor, in place of a login.</param>
    /// <param name="accountName">The exported account's system user name, which an operator searches for.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="succeeded">Whether the package, or its head, was served.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    /// <remarks>
    /// The actor names the grant's id and never the token. No user id is recorded, because a grant is not a user.
    /// </remarks>
    public async Task RecordPullAsync(
        Guid grantId,
        string accountName,
        string ipAddress,
        string userAgent,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        await _auditWriter.WriteAsync(
            new AuditEntry(
                null,
                GrantActorPrefix + grantId.ToString("N"),
                MigrationAuditActions.ExportGrantPulled,
                accountName,
                ipAddress,
                userAgent,
                succeeded),
            cancellationToken);
    }

    /// <summary>Records work the panel did with nobody asking, under its own name.</summary>
    /// <param name="action">The action name, one of <see cref="MigrationAuditActions"/>.</param>
    /// <param name="subject">What the work was done to: the exported account's system user name.</param>
    /// <param name="succeeded">Whether the work took effect.</param>
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

    /// <summary>Records an operation on a remote server that took effect.</summary>
    /// <param name="action">The action name, one of <see cref="MigrationAuditActions"/>.</param>
    /// <param name="subject">What the entry is about: the server's host and id, or what was attempted. Never a key.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    public async Task RecordSuccessAsync(
        string action,
        string subject,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        await WriteAsync(action, subject, ipAddress, userAgent, succeeded: true, cancellationToken);
    }

    /// <summary>Records an operation on a remote server that was refused or failed.</summary>
    /// <param name="action">The action that was attempted.</param>
    /// <param name="subject">What the entry is about: the server's host and id, or what was attempted. Never a key.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    public async Task RecordFailureAsync(
        string action,
        string subject,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        await WriteAsync(action, subject, ipAddress, userAgent, succeeded: false, cancellationToken);
    }

    /// <summary>Builds and stores one entry under the signed-in caller's name.</summary>
    /// <param name="action">The action name.</param>
    /// <param name="subject">What the entry is about: the server's host and id, or what was attempted. Never a key.</param>
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
