using Maran.Host.Authorization;
using Maran.Modules.Accounts.Queries.CountAccounts;
using Maran.Modules.Backups.Queries.CountFailedBackups;
using Maran.Modules.Databases.Queries.CountDatabases;
using Maran.Modules.Firewall.Queries.CountBans;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Queries.ListRecentAuditEvents;
using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Queries.GetClockSync;
using Maran.Modules.Monitoring.Queries.GetHostMetrics;
using Maran.Modules.Monitoring.Queries.ListServiceStatuses;
using Maran.Modules.Sites.Queries.CountSites;
using Maran.Modules.Ssl.Queries.CountExpiringCertificates;
using Maran.Modules.Tasks.Queries.CountFailedTasks;
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
/// Dispatching <see cref="CountAccountsQuery"/> from here therefore runs a query whose HTTP surface
/// is administrators-only with no policy in the way, so this endpoint supplies the decision itself:
/// the route requires a signed-in caller, and every server-wide section is read only after
/// <see cref="ICurrentUser.IsAdmin"/> is confirmed true. The check comes BEFORE any dispatch, not
/// after — a response filtered on the way out would already have read the data. Any section added
/// here later inherits that rule or it does not go in.
/// </para>
/// <para>
/// A non-administrator receives the shape with every administrator-only section null. That is not
/// an oversight: a customer's own landing screen is the client zone, which is issue #49's subject,
/// and a second one invented here is a thing that issue would then have to undo.
/// </para>
/// <para>
/// <b>One failing section marks itself unavailable and nothing else.</b> The resources and services
/// come from the agent and the rest from the database, so an unreachable agent must not blank the
/// counts — a screen that goes dark all at once tells an operator less than one that says which half
/// is missing. A section that could not be read is <see cref="DashboardSection.Unavailable{T}"/>,
/// never an empty list, a zero or a null: those are real answers, and an attention figure read off a
/// failed listing as zero is how the screen once said "nothing needs attention" about a source it
/// never checked (issue #248).
/// </para>
/// <para>
/// UNOBSERVED HERE: this endpoint reports what the modules answer. It does not verify the host
/// against them — a count is as right as the module's table, and the resources are as right as the
/// agent's reading.
/// </para>
/// </remarks>
public static class DashboardEndpoint
{
    /// <summary>Pre-compiled log delegate for a section whose query threw rather than refused.</summary>
    private static readonly Action<ILogger, string, Exception?> LogUnreadableSection =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(DashboardEndpoint)),
            "Dashboard section {Query} could not be read");

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
            async (
                IMessageBus bus,
                ICurrentUser currentUser,
                IClock clock,
                ILoggerFactory loggers,
                CancellationToken cancellationToken) =>
            {
                var logger = loggers.CreateLogger(typeof(DashboardEndpoint));
                return Results.Ok(await DescribeAsync(bus, logger, currentUser, clock, cancellationToken));
            })
            // Authenticated, never anonymous: /api/v1/modules may be read by anybody because it
            // describes what this build composed, while this route describes what the server holds.
            // Any signed-in caller, customer included — the payload is shaped by who is asking — so
            // the policy is named rather than left to the administrator-only fallback.
            .RequireAuthorization(RolePolicies.AnyAuthenticated);
        return endpoints;
    }

    /// <summary>Assembles the caller's view of the server.</summary>
    /// <param name="bus">The bus every module query is dispatched through.</param>
    /// <param name="logger">Where a section that threw is recorded.</param>
    /// <param name="currentUser">The caller, whose administrator state decides what is read at all.</param>
    /// <param name="clock">The panel's clock, which the certificate horizon is measured from.</param>
    /// <param name="cancellationToken">Cancels every dispatch.</param>
    /// <returns>The payload for this caller.</returns>
    private static async Task<DashboardDto> DescribeAsync(
        IMessageBus bus,
        ILogger logger,
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
                Services: null,
                Counts: null,
                Attention: null,
                RecentAudit: null);
        }

        // Together rather than one after another: they are one screen, and eleven sequential
        // round-trips would make the first paint the sum of them. Each task already carries its own
        // failure, so WhenAll cannot throw on behalf of one of them, and every await below it reads a
        // task that has already finished.
        var resources = ReadAsync<HostMetricsDto>(bus, logger, new GetHostMetricsQuery(), cancellationToken);
        var services = ReadAsync<IReadOnlyList<ServiceStatusDto>>(
            bus, logger, new ListServiceStatusesQuery(), cancellationToken);
        var accounts = ReadAsync<int>(bus, logger, new CountAccountsQuery(), cancellationToken);
        var sites = ReadAsync<int>(bus, logger, new CountSitesQuery(), cancellationToken);
        var databases = ReadAsync<int>(bus, logger, new CountDatabasesQuery(), cancellationToken);
        var certificates = ReadAsync<int>(
            bus,
            logger,
            new CountExpiringCertificatesQuery(clock.UtcNow.Add(CertificateExpiryHorizon)),
            cancellationToken);
        var failedBackups = ReadAsync<int>(bus, logger, new CountFailedBackupsQuery(), cancellationToken);
        var bans = ReadAsync<int>(bus, logger, new CountBansQuery(), cancellationToken);
        var failedTasks = ReadAsync<int>(bus, logger, new CountFailedTasksQuery(), cancellationToken);
        var clockSync = ReadAsync<ClockSyncDto>(bus, logger, new GetClockSyncQuery(), cancellationToken);
        var audit = ReadAsync<IReadOnlyList<AuditEventDto>>(
            bus, logger, new ListRecentAuditEventsQuery(RecentAuditEntries), cancellationToken);

        await Task.WhenAll(
            resources,
            services,
            accounts,
            sites,
            databases,
            certificates,
            failedBackups,
            bans,
            failedTasks,
            clockSync,
            audit);

        return new DashboardDto(
            IsAdministrator: true,
            Resources: await resources,
            Services: await services,

            // Each count comes from the module's own count query, never from its list: a paged
            // listing holds one page, not the total. A module that did not answer is Unavailable,
            // never zero.
            Counts: new DashboardCountsDto(
                Accounts: await accounts,
                Sites: await sites,
                Databases: await databases),

            Attention: new DashboardAttentionDto(
                // Already-expired certificates are included, deliberately: the horizon is an upper
                // bound on "soon", and a certificate that lapsed yesterday has not stopped needing
                // attention by becoming late.
                CertificatesExpiringSoon: await certificates,
                FailedBackups: await failedBackups,

                BannedAddresses: await bans,
                FailedTasks: await failedTasks,
                Clock: await clockSync),

            RecentAudit: await audit);
    }

    /// <summary>
    /// Dispatches one query and answers with its value as a loaded section, or with an unavailable
    /// one when the module refused or failed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The failure is not swallowed: it becomes the section's state, which the screen draws as "could
    /// not be read" — the one honest thing it can say about it — and one module's bad day does not take
    /// the whole landing page down. The caller is an administrator by this point, so a refusal is a
    /// genuine failure rather than a permission boundary, and the module that refused has already
    /// logged why, in its own words, which is where that belongs. A query that threw instead is logged
    /// here, since nothing else would.
    /// </para>
    /// <para>
    /// A count is asked of the module as a count — one <c>count(*)</c> — and never read off a page of
    /// a listing, which since lists are paged would be the size of one page and not the total.
    /// </para>
    /// </remarks>
    /// <typeparam name="TResult">What the query answers with.</typeparam>
    /// <param name="bus">The bus to dispatch on.</param>
    /// <param name="logger">Where a query that threw is recorded.</param>
    /// <param name="query">The query to send.</param>
    /// <param name="cancellationToken">Cancels the dispatch.</param>
    /// <returns>The section, loaded or unavailable.</returns>
    private static async Task<DashboardSection<TResult>> ReadAsync<TResult>(
        IMessageBus bus,
        ILogger logger,
        object query,
        CancellationToken cancellationToken)
    {
        try
        {
            var answer = await bus.InvokeAsync<Result<TResult>>(query, cancellationToken);
            return answer.IsSuccess
                ? DashboardSection.Loaded<TResult>(answer.Value)
                : DashboardSection.Unavailable<TResult>();
        }
        catch (OperationCanceledException)
        {
            // The caller gave up or the request was cancelled; not this endpoint's to report.
            throw;
        }
        catch (Exception exception)
        {
            // Recorded here, because a query that THREW logged nothing of its own; the screen is told
            // through the section's state (see the remarks).
            LogUnreadableSection(logger, query.GetType().Name, exception);
            return DashboardSection.Unavailable<TResult>();
        }
    }
}
