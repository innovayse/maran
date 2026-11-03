using Maran.Host.Authorization;
using Maran.Modules.Accounts.Queries.CountAccounts;
using Maran.Modules.Databases.Queries.CountDatabases;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Queries.ListRecentAuditEvents;
using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Queries.GetHostMetrics;
using Maran.Modules.Monitoring.Queries.ListServiceStatuses;
using Maran.Modules.Sites.Queries.CountSites;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;
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
/// <see cref="ICurrentUser.Caller"/> is confirmed to be an <see cref="AdministratorCaller"/>. The check comes BEFORE any dispatch, not
/// after — a response filtered on the way out would already have read the data. Any section added
/// here later inherits that rule or it does not go in.
/// </para>
/// <para>
/// A non-administrator receives <see cref="CustomerDashboardDto"/>, which has no section at all.
/// That is not an oversight: a customer's own landing screen is the client zone, which is issue #49's subject,
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
                // Declared as the union's base: only then does the serializer write `audience`.
                return Results.Ok<DashboardDto>(
                    await DescribeAsync(bus, logger, currentUser, clock, cancellationToken));
            })
            // Authenticated, never anonymous: /api/v1/modules may be read by anybody because it
            // describes what this build composed, while this route describes what the server holds.
            // Any signed-in caller, customer included — the payload is shaped by who is asking — so
            // the policy is named rather than left to the administrator-only fallback.
            .RequireAuthorization(RolePolicies.AnyAuthenticated)
            // A read the shell opens on: an operator viewing as a login lands here first (issue #334).
            .WithMetadata(new ViewAsAccessAttribute(ViewAsAccess.Read));
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
        // Before any dispatch. An unauthenticated caller — which this route refuses anyway — and a
        // customer are both not an AdministratorCaller.
        if (currentUser.Caller is not AdministratorCaller)
        {
            return new CustomerDashboardDto();
        }

        // Together rather than one after another: they are one screen, and eleven sequential
        // round-trips would make the first paint the sum of them. Each task already carries its own
        // failure, so WhenAll cannot throw on behalf of one of them, and every await below it reads a
        // task that has already finished.
        var resources = DashboardSectionReader.ReadAsync<HostMetricsDto>(
            bus, logger, new GetHostMetricsQuery(), cancellationToken);
        var services = DashboardSectionReader.ReadAsync<IReadOnlyList<ServiceStatusDto>>(
            bus, logger, new ListServiceStatusesQuery(), cancellationToken);
        var accounts = DashboardSectionReader.ReadAsync<int>(
            bus, logger, new CountAccountsQuery(), cancellationToken);
        var sites = DashboardSectionReader.ReadAsync<int>(
            bus, logger, new CountSitesQuery(), cancellationToken);
        var databases = DashboardSectionReader.ReadAsync<int>(
            bus, logger, new CountDatabasesQuery(), cancellationToken);
        var attention = AttentionReader.ReadAsync(bus, logger, clock, cancellationToken);
        var audit = DashboardSectionReader.ReadAsync<IReadOnlyList<AuditEventDto>>(
            bus, logger, new ListRecentAuditEventsQuery(RecentAuditEntries), cancellationToken);

        await Task.WhenAll(
            resources,
            services,
            accounts,
            sites,
            databases,
            attention,
            audit);

        return new AdministratorDashboardDto(
            Resources: await resources,
            Services: await services,

            // Each count comes from the module's own count query, never from its list: a paged
            // listing holds one page, not the total. A module that did not answer is Unavailable,
            // never zero.
            Counts: new DashboardCountsDto(
                Accounts: await accounts,
                Sites: await sites,
                Databases: await databases),

            Attention: await attention,

            RecentAudit: await audit);
    }
}
