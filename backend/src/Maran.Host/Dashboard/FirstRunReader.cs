using Maran.Modules.Accounts.Queries.CountAccounts;
using Maran.Modules.Accounts.Queries.CountCustomPlans;
using Maran.Modules.Backups.Queries.CountConfiguredBackupDestinations;
using Maran.Modules.Sites.Queries.CountSites;
using Maran.Modules.Ssl.Queries.CountCertificates;
using Wolverine;

namespace Maran.Host.Dashboard;

/// <summary>
/// Reads the first-run checklist: five counts, one per step, each turned into "has the administrator
/// done this".
/// </summary>
/// <remarks>
/// <para>
/// <b>What "done" means is the module's count, and the seeds are already out of it.</b> A plan is
/// done by a plan that is not one of the three seeded; a backup step by a destination other than the
/// seeded local one or an enabled host-wide schedule. Accounts, sites and certificates are done by
/// any row, because nothing seeds them.
/// </para>
/// <para>
/// <b>Authorization is the caller's, and it comes first</b>, exactly as for <see cref="AttentionReader"/>:
/// the queries run on the bus where no policy applies, so the caller confirms the caller is an
/// administrator before calling this.
/// </para>
/// </remarks>
public static class FirstRunReader
{
    /// <summary>Reads every step together; one that cannot be read marks only itself.</summary>
    /// <param name="bus">The bus every module query is dispatched through.</param>
    /// <param name="logger">Where a source whose query threw is recorded.</param>
    /// <param name="cancellationToken">Cancels every dispatch.</param>
    /// <returns>The checklist.</returns>
    public static async Task<FirstRunChecklistDto> ReadAsync(
        IMessageBus bus,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Together: five independent single-count reads, and the card is one screen.
        var plans = DashboardSectionReader.ReadAsync<int>(
            bus, logger, new CountCustomPlansQuery(), cancellationToken);
        var accounts = DashboardSectionReader.ReadAsync<int>(
            bus, logger, new CountAccountsQuery(), cancellationToken);
        var sites = DashboardSectionReader.ReadAsync<int>(
            bus, logger, new CountSitesQuery(), cancellationToken);
        var backups = DashboardSectionReader.ReadAsync<int>(
            bus, logger, new CountConfiguredBackupDestinationsQuery(), cancellationToken);
        var certificates = DashboardSectionReader.ReadAsync<int>(
            bus, logger, new CountCertificatesQuery(), cancellationToken);

        await Task.WhenAll(plans, accounts, sites, backups, certificates);

        return Assemble(
            await plans,
            await accounts,
            await sites,
            await backups,
            await certificates);
    }

    /// <summary>Turns the five counts into the checklist.</summary>
    /// <param name="customPlans">Plans the administrator made.</param>
    /// <param name="accounts">Hosting accounts.</param>
    /// <param name="sites">Sites.</param>
    /// <param name="configuredBackups">What was configured beyond the seeded backup destination.</param>
    /// <param name="certificates">Site certificates.</param>
    /// <returns>The checklist, complete only when every step was read and is done.</returns>
    public static FirstRunChecklistDto Assemble(
        DashboardSection<int> customPlans,
        DashboardSection<int> accounts,
        DashboardSection<int> sites,
        DashboardSection<int> configuredBackups,
        DashboardSection<int> certificates)
    {
        var steps = new[]
        {
            Step(FirstRunStepKey.Plan, customPlans, FirstRunRoute.Plans),
            Step(FirstRunStepKey.Account, accounts, FirstRunRoute.Accounts),
            Step(FirstRunStepKey.Site, sites, FirstRunRoute.Sites),
            Step(FirstRunStepKey.BackupDestination, configuredBackups, FirstRunRoute.BackupDestinations),
            Step(FirstRunStepKey.Certificate, certificates, FirstRunRoute.Certificates),
        };

        var complete = steps.All(step =>
        {
            return step.Done.Match(
                done =>
                {
                    return done;
                },
                () =>
                {
                    return false;
                });
        });

        return new FirstRunChecklistDto(steps, complete);
    }

    /// <summary>Builds one step whose done state is "its count is above zero".</summary>
    /// <param name="key">Which step.</param>
    /// <param name="count">The step's count, loaded or unavailable.</param>
    /// <param name="route">Where the step is done.</param>
    /// <returns>The step.</returns>
    private static FirstRunStepDto Step(FirstRunStepKey key, DashboardSection<int> count, FirstRunRoute route)
    {
        return new FirstRunStepDto(
            key,
            count.Map(value =>
            {
                return value > 0;
            }),
            route);
    }
}
