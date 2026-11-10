using Maran.Host.Demo.Seeding.Exceptions;
using Maran.Host.Demo.Seeding.Interfaces;
using Maran.Host.Demo.Seeding.Models;
using Maran.Host.Demo.Seeding.Services;
using Maran.Modules.Apps.Commands.InstallApp;
using Maran.Modules.Apps.Common;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.Demo.Seeding.Steps;

/// <summary>
/// Installs the web server and the PHP versions the world's sites run on that the server does not already
/// carry, through the same command the app store uses, and waits for the installs to finish so the stack
/// is ready before the first site is created (issue #517).
/// </summary>
/// <remarks>
/// The install runs as a recorded task on the panel's bus, like every install, so the step cannot return
/// when the command is accepted: the Sites module refuses a site while no web server is installed, and the
/// accepted answer says only that the work was queued. It reads the ledger until every wanted app is
/// installed, and fails the seed, naming the app, when one ends failed or the wait runs out.
/// </remarks>
public sealed class AppStackSeedStep : IDemoSeedStep
{
    /// <summary>The web server the demonstration runs its sites on.</summary>
    private const string WebServer = "nginx";

    /// <summary>How long the installs may take before the seed gives up; the simulated agent takes seconds.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(2);

    /// <summary>How often the ledger is read while waiting.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>The Apps module's ledger.</summary>
    private readonly AppsDbContext _apps;

    /// <summary>Runs commands through the real handlers.</summary>
    private readonly DemoSeedCommandRunner _runner;

    /// <summary>Creates the step.</summary>
    /// <param name="apps">The Apps module's ledger.</param>
    /// <param name="runner">Runs commands through the real handlers.</param>
    public AppStackSeedStep(AppsDbContext apps, DemoSeedCommandRunner runner)
    {
        _apps = apps;
        _runner = runner;
    }

    /// <inheritdoc />
    public string Name
    {
        get { return "app-stack"; }
    }

    /// <inheritdoc />
    public async Task RunAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        var wanted = new List<string> { WebServer };
        wanted.AddRange(context.World.PhpVersions.Select(version => { return "php-" + version; }));

        var carried = await InstalledAsync(cancellationToken);
        foreach (var appId in wanted.Where(appId => { return !carried.Contains(appId); }))
        {
            await _runner.RunAsync<AppOperationAcceptedDto>(
                Name,
                new InstallAppCommand(appId, DemoSeedCommandRunner.Origin, DemoSeedCommandRunner.Agent),
                cancellationToken);
        }

        var waited = TimeSpan.Zero;
        while (true)
        {
            var rows = await _apps.InstalledApps.AsNoTracking().ToListAsync(cancellationToken);
            var failed = rows.FirstOrDefault(row => { return wanted.Contains(row.AppId) && row.State == AppState.Failed; });
            if (failed is not null)
            {
                throw new DemoSeedException(Name, "AppsOperationFailed");
            }

            var done = wanted.All(appId => { return rows.Any(row => { return row.AppId == appId && row.IsInstalled; }); });
            if (done)
            {
                return;
            }

            if (waited >= Deadline)
            {
                throw new DemoSeedException(Name, "AppsOutcomeUnobserved");
            }

            await Task.Delay(PollInterval, cancellationToken);
            waited += PollInterval;
        }
    }

    /// <summary>Reads the ids of the apps the ledger holds as installed.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The installed ids.</returns>
    private async Task<HashSet<string>> InstalledAsync(CancellationToken cancellationToken)
    {
        var ids = await _apps.InstalledApps
            .AsNoTracking()
            .Where(row => row.State == AppState.Installed)
            .Select(row => row.AppId)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet(StringComparer.Ordinal);
    }
}
