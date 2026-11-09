using Maran.Host.Demo.Seeding.Interfaces;
using Maran.Host.Demo.Seeding.Models;
using Maran.Host.Demo.Seeding.Services;
using Maran.Modules.Sites.Commands.CreateSite;
using Maran.Modules.Sites.Commands.DisableSite;
using Maran.Modules.Sites.Common;

namespace Maran.Host.Demo.Seeding.Steps;

/// <summary>Creates every account's sites, then disables the ones the world leaves disabled.</summary>
public sealed class SiteSeedStep : IDemoSeedStep
{
    /// <summary>Runs commands through the real handlers.</summary>
    private readonly DemoSeedCommandRunner _runner;

    /// <summary>Creates the step.</summary>
    /// <param name="runner">Runs commands through the real handlers.</param>
    public SiteSeedStep(DemoSeedCommandRunner runner)
    {
        _runner = runner;
    }

    /// <inheritdoc />
    public string Name
    {
        get { return "sites"; }
    }

    /// <inheritdoc />
    public async Task RunAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        foreach (var account in context.World.Accounts)
        {
            foreach (var site in account.Sites)
            {
                var created = await _runner.RunAsync<SiteDto>(
                    Name,
                    new CreateSiteCommand(
                        context.AccountId(account.Name),
                        site.Domain,
                        site.BackendType,
                        site.Aliases,
                        site.PhpVersion,
                        site.ProxyUpstream,
                        DemoSeedCommandRunner.Origin,
                        DemoSeedCommandRunner.Agent),
                    cancellationToken);
                context.SiteIds[site.Domain] = created.Id;
            }
        }

        foreach (var site in context.World.Sites.Where(site => { return !site.Enabled; }))
        {
            await _runner.RunAsync<SiteDto>(
                Name,
                new DisableSiteCommand(
                    context.SiteIds[site.Domain],
                    DemoSeedCommandRunner.Origin,
                    DemoSeedCommandRunner.Agent),
                cancellationToken);
        }
    }
}
