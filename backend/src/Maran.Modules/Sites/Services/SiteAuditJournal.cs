using System.Diagnostics;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Writes this module's audit entries, so every Sites handler records the same shape and no handler
/// has to remember what an entry is made of.
/// </summary>
/// <remarks>
/// It exists chiefly so that FAILURES are journalled as reliably as successes.
/// <see cref="AuditEntry"/> says outright that "failures are the half of the journal worth reading",
/// and a refused provisioning, a plan limit hit or a cross-tenant probe are precisely the events an
/// operator later needs. Written inline, the failure entry is the one every early <c>return</c>
/// walks past — which is what happened here before this type existed: all five handlers journalled
/// only their success path.
///
/// The subject is always the site's domain, never the agent's output: that text may name absolute
/// paths on the host, and the journal is never deleted (rules/security.md).
/// </remarks>
public sealed class SiteAuditJournal : ISystemAuditJournal
{
    /// <summary>This module's short name, from which its system actor name is built.</summary>
    public const string ModuleName = "sites";

    /// <summary>The panel's append-only journal.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>The authenticated principal, recorded as the actor of every entry.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the journal wrapper.</summary>
    /// <param name="auditWriter">The panel's append-only journal.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public SiteAuditJournal(IAuditWriter auditWriter, ICurrentUser currentUser)
    {
        _auditWriter = auditWriter;
        _currentUser = currentUser;
    }

    /// <summary>Records an operation that took effect.</summary>
    /// <param name="action">The action name, one of the Sdk's <c>*AuditActions</c> constants.</param>
    /// <param name="domain">The site's primary domain — the subject an operator will search for.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    public async Task RecordSuccessAsync(
        string action,
        string domain,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        await WriteAsync(action, domain, ipAddress, userAgent, succeeded: true, cancellationToken);
    }

    /// <summary>Records that a site's per-site options were replaced.</summary>
    /// <param name="domain">The site's primary domain — the subject an operator will search for.</param>
    /// <param name="changedGroups">The option groups whose content changed, from <c>SiteOptionGroups</c>.</param>
    /// <param name="basicAuthUserNames">The names of the basic-authentication users the site has after the change.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <remarks>
    /// The subject states WHAT changed and who may log in, and nothing a reader could use to get in: the
    /// groups are fixed words and the users are names. A password, a hash and the contents of an address
    /// list never reach the journal, which is never deleted (rules/security.md item 8).
    /// </remarks>
    public async Task RecordOptionsChangedAsync(
        string domain,
        IReadOnlyList<string> changedGroups,
        IReadOnlyList<string> basicAuthUserNames,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        var subject = $"{domain}: {(changedGroups.Count == 0 ? "no change" : string.Join(", ", changedGroups))}";
        if (basicAuthUserNames.Count > 0)
        {
            subject += $"; basic-auth users: {string.Join(", ", basicAuthUserNames)}";
        }

        await WriteAsync(SiteAuditActions.SiteOptionsChanged, subject, ipAddress, userAgent, succeeded: true, cancellationToken);
    }

    /// <summary>Records that a PHP extension was, or was asked to be, switched for one PHP version.</summary>
    /// <param name="version">The validated PHP version, e.g. <c>8.3</c>.</param>
    /// <param name="extension">The validated catalogue name, e.g. <c>gd</c>.</param>
    /// <param name="enabled">The state that was asked for.</param>
    /// <param name="succeeded">Whether the extension is in the requested state.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <remarks>
    /// The subject is the version, the catalogue name and the requested state (<c>8.3: gd enabled</c>),
    /// built here so the success and the failure of one request carry one spelling. Both parts have
    /// passed the command validator before a handler runs, and neither is a package name or any text
    /// the agent produced: that can name paths on the host, and the journal is never deleted
    /// (rules/security.md item 8).
    /// </remarks>
    public async Task RecordPhpExtensionChangedAsync(
        string version,
        string extension,
        bool enabled,
        bool succeeded,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        var subject = $"{version}: {extension} {(enabled ? "enabled" : "disabled")}";

        await WriteAsync(SiteAuditActions.PhpExtensionChanged, subject, ipAddress, userAgent, succeeded, cancellationToken);
    }

    /// <summary>Records that a customer's PHP settings were replaced (issue #138).</summary>
    /// <param name="accountName">The account's system user name — the subject an operator will search for.</param>
    /// <param name="changedNames">The settings added, changed or removed, by their php.ini names.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <remarks>
    /// The subject names WHICH settings changed and not their values: the values are the customer's
    /// configuration, not evidence, and the names are enough to see who touched what.
    /// </remarks>
    public async Task RecordPhpSettingsChangedAsync(
        string accountName,
        IReadOnlyList<string> changedNames,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        var subject = $"{accountName}: {(changedNames.Count == 0 ? "no change" : string.Join(", ", changedNames))}";

        await WriteAsync(SiteAuditActions.PhpSettingsChanged, subject, ipAddress, userAgent, succeeded: true, cancellationToken);
    }

    /// <summary>Records an operation that was refused, and why in machine terms.</summary>
    /// <param name="action">The action that was attempted.</param>
    /// <param name="subject">
    /// What it was attempted on. The domain where one is known; otherwise the identifier the caller
    /// supplied, so that a probe for a site the caller may not see still leaves a trace naming what
    /// was probed for.
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

    /// <summary>Records something the panel did on its own initiative, with nobody signed in.</summary>
    /// <param name="action">The action name, one of the Sdk's <c>*AuditActions</c> constants.</param>
    /// <param name="subject">The site's primary domain — the subject an operator will search for.</param>
    /// <param name="succeeded">Whether the operation took effect.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <remarks>
    /// Outside a request <see cref="ICurrentUser"/> reports no one, which is what a failed anonymous request
    /// records too; <see cref="SystemAuditEntry"/> names the panel instead, so an operator can tell the
    /// hourly log pass from someone probing the API.
    /// </remarks>
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
