using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Queries.ListAccounts;
using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Queries.ListBackups;
using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Queries.ListDatabases;
using Maran.Modules.Firewall.Common;
using Maran.Modules.Firewall.Queries.ListBans;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Queries.ListAuditEvents;
using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Queries.GetHostMetrics;
using Maran.Modules.Monitoring.Queries.ListServiceStatuses;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Queries.ListSites;
using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Queries.ListCertificates;
using Maran.Modules.Tasks.Common;
using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Queries.ListTasks;
using Maran.SharedKernel.Results;
using Wolverine;

namespace Maran.Host.Dashboard;

/// <summary>
/// Publishes what the panel's landing screen shows: what this server is doing, whether what must be
/// running is running, what it holds, what needs attention, and what just happened.
/// </summary>
/// <remarks>
/// <para>
/// Lives in the Host because it is composition, like <c>ModulesEndpoint</c>: it reads the queries
/// nine modules ALREADY publish and assembles one answer. No module gains a query for it, and no
/// module learns that another exists — <c>ModuleIsolationTests</c> forbids that, and the Host is the
/// one place allowed to see them all.
/// </para>
/// <para>
/// <b>Authorization is this file's one dangerous responsibility, so it is stated rather than
/// implied.</b> Every module's CONTROLLER carries its own policy; its query HANDLER carries none.
/// Dispatching <see cref="ListAccountsQuery"/> from here therefore runs a query whose HTTP surface
/// is administrators-only with no policy in the way, so this endpoint supplies the decision itself:
/// the route requires a signed-in caller, and every server-wide section is read only after
/// <see cref="ICurrentUser.IsAdmin"/> is confirmed true. The check comes BEFORE any dispatch, not
/// after — a response filtered on the way out would already have read the data. Any section added
/// here later inherits that rule or it does not go in.
/// </para>
/// <para>
/// A non-administrator receives the shape with every administrator-only section empty. That is not
/// an oversight: a customer's own landing screen is the client zone, which is issue #49's subject,
/// and a second one invented here is a thing that issue would then have to undo.
/// </para>
/// <para>
/// <b>One failing section empties itself and nothing else.</b> The resources and services come from
/// the agent and the rest from the database, so an unreachable agent must not blank the counts — a
/// screen that goes dark all at once tells an operator less than one that says which half is
/// missing. This is the same split, and for the same reason, that the monitoring store makes
/// between its charts and its per-account disk read.
/// </para>
/// <para>
/// UNOBSERVED HERE: this endpoint reports what the modules answer. It does not verify the host
/// against them — a count is as right as the module's table, and the resources are as right as the
/// agent's reading.
/// </para>
/// </remarks>
public static class DashboardEndpoint
{
    /// <summary>
    /// How near expiry a certificate has to be to count as needing attention.
    /// </summary>
    /// <remarks>
    /// Thirty days because that is comfortably more than the renewal window of the certificates the
    /// panel issues — ACME renewal is attempted well inside it — so anything still listed here has
    /// had renewal fail repeatedly, or is a certificate the operator uploaded and must replace by
    /// hand. A shorter horizon would surface problems too late to act on; a longer one would list
    /// certificates that are going to renew themselves tomorrow, and a number an operator learns to
    /// ignore is worse than no number.
    /// </remarks>
    private static readonly TimeSpan CertificateExpiryHorizon = TimeSpan.FromDays(30);

    /// <summary>How many audit entries the landing page shows.</summary>
    /// <remarks>
    /// Few enough to read without scrolling on the screen they share with everything else; the
    /// journal itself is one click away and is where a reader who wants more should end up.
    /// </remarks>
    private const int RecentAuditEntries = 5;

    /// <summary>Maps <c>GET /api/v1/dashboard</c>.</summary>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapDashboard(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/v1/dashboard",
            async (IMessageBus bus, ICurrentUser currentUser, IClock clock, CancellationToken cancellationToken) =>
                Results.Ok(await DescribeAsync(bus, currentUser, clock, cancellationToken)))
            // Authenticated, never anonymous: /api/v1/modules may be read by anybody because it
            // describes what this build composed, while this route describes what the server holds.
            .RequireAuthorization();
        return endpoints;
    }

    /// <summary>Assembles the caller's view of the server.</summary>
    /// <param name="bus">The bus every module query is dispatched through.</param>
    /// <param name="currentUser">The caller, whose administrator state decides what is read at all.</param>
    /// <param name="clock">The panel's clock, which the certificate horizon is measured from.</param>
    /// <param name="cancellationToken">Cancels every dispatch.</param>
    /// <returns>The payload for this caller.</returns>
    private static async Task<DashboardDto> DescribeAsync(
        IMessageBus bus,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        // Before any dispatch. ICurrentUser fails closed, so an unauthenticated caller — which this
        // route refuses anyway — and a customer both read false here.
        if (!currentUser.IsAdmin)
        {
            return new DashboardDto(
                IsAdministrator: false,
                Resources: null,
                Services: [],
                Counts: null,
                Attention: null,
                RecentAudit: []);
        }

        // Together rather than one after another: they are one screen, and nine sequential
        // round-trips would make the first paint the sum of them. Each task already carries its own
        // failure, so WhenAll cannot throw on behalf of one of them.
        var resources = ReadAsync<HostMetricsDto>(bus, new GetHostMetricsQuery(), cancellationToken);
        var services = ReadListAsync<ServiceStatusDto>(bus, new ListServiceStatusesQuery(), cancellationToken);
        var accounts = ReadListAsync<AccountDto>(bus, new ListAccountsQuery(), cancellationToken);
        var sites = ReadListAsync<SiteDto>(bus, new ListSitesQuery(), cancellationToken);
        var databases = ReadListAsync<DatabaseDto>(bus, new ListDatabasesQuery(), cancellationToken);
        var certificates = ReadListAsync<CertificateDto>(bus, new ListCertificatesQuery(), cancellationToken);
        var backups = ReadListAsync<BackupDto>(bus, new ListBackupsQuery(), cancellationToken);
        var bans = ReadListAsync<BanDto>(bus, new ListBansQuery(), cancellationToken);
        var tasks = ReadListAsync<PanelTaskDto>(bus, new ListTasksQuery(), cancellationToken);
        var audit = ReadListAsync<AuditEventDto>(bus, new ListAuditEventsQuery(RecentAuditEntries), cancellationToken);

        await Task.WhenAll(
            resources,
            services,
            accounts,
            sites,
            databases,
            certificates,
            backups,
            bans,
            tasks,
            audit);

        var expiringBefore = clock.UtcNow.Add(CertificateExpiryHorizon);

        return new DashboardDto(
            IsAdministrator: true,
            Resources: resources.Result,
            Services: services.Result,

            // Counted from the listings the modules already answer, rather than from count queries
            // added for this screen: these are three database reads of tables measured in hundreds
            // of rows on the largest servers this panel targets, and a second read path per module
            // would be a second thing to keep true.
            Counts: new DashboardCountsDto(
                Accounts: accounts.Result.Count,
                Sites: sites.Result.Count,
                Databases: databases.Result.Count),

            Attention: new DashboardAttentionDto(
                // Already-expired certificates are included, deliberately: the horizon is an upper
                // bound on "soon", and a certificate that lapsed yesterday has not stopped needing
                // attention by becoming late.
                CertificatesExpiringSoon: certificates.Result.Count(certificate =>
                {
                    return certificate.NotAfter <= expiringBefore;
                }),
                FailedBackups: backups.Result.Count(backup =>
                {
                    return backup.Status == BackupStatus.Failed;
                }),
                BannedAddresses: bans.Result.Count,
                FailedTasks: tasks.Result.Count(task =>
                {
                    return task.Status == PanelTaskStatus.Failed;
                })),

            RecentAudit: audit.Result);
    }

    /// <summary>
    /// Dispatches one query and answers with its value, or with <c>null</c> when it refused or failed.
    /// </summary>
    /// <remarks>
    /// The refusal is swallowed ON PURPOSE and only here: a section that cannot be read renders as
    /// absent, which is what the payload's nullability is for, and one module's bad day must not
    /// take the whole landing page down. The caller is an administrator by this point, so a refusal
    /// is a genuine failure rather than a permission boundary — and the module that refused has
    /// already logged why, in its own words, which is where that belongs.
    /// </remarks>
    /// <typeparam name="TResult">What the query answers with.</typeparam>
    /// <param name="bus">The bus to dispatch on.</param>
    /// <param name="query">The query to send.</param>
    /// <param name="cancellationToken">Cancels the dispatch.</param>
    /// <returns>The value, or <c>null</c>.</returns>
    private static async Task<TResult?> ReadAsync<TResult>(
        IMessageBus bus,
        object query,
        CancellationToken cancellationToken)
        where TResult : class
    {
        try
        {
            var answer = await bus.InvokeAsync<Result<TResult>>(query, cancellationToken);
            return answer.IsSuccess ? answer.Value : null;
        }
        catch (OperationCanceledException)
        {
            // The caller gave up or the request was cancelled; not this endpoint's to report.
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Dispatches one listing query and answers with its items, or with an empty list.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ReadAsync{TResult}"/> so that a list section is never null:
    /// the page draws "none" and "could not be read" the same way for a list, and a nullable list
    /// would give every caller a third state to handle for no gain. The sections where the
    /// difference is real are the ones carrying numbers, and those are nullable.
    /// </remarks>
    /// <typeparam name="TItem">The item type the listing answers with.</typeparam>
    /// <param name="bus">The bus to dispatch on.</param>
    /// <param name="query">The query to send.</param>
    /// <param name="cancellationToken">Cancels the dispatch.</param>
    /// <returns>The items, or an empty list.</returns>
    private static async Task<IReadOnlyList<TItem>> ReadListAsync<TItem>(
        IMessageBus bus,
        object query,
        CancellationToken cancellationToken)
    {
        var answer = await ReadAsync<IReadOnlyList<TItem>>(bus, query, cancellationToken);
        return answer ?? [];
    }
}
