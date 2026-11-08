using Maran.Host.Demo.Seeding.Exceptions;
using Maran.Host.Demo.Seeding.Interfaces;
using Maran.Host.Demo.Seeding.Models;
using Maran.Host.Seeding;

namespace Maran.Host.Demo.Seeding.Steps;

/// <summary>
/// Runs the reference-data seeds the panel runs at every start (standard plans, the firewall whitelist
/// of the operator's own address, the default backup destination). A command-line run starts no hosted
/// service, so without this step the database would hold none of them.
/// </summary>
public sealed class ReferenceDataSeedStep : IDemoSeedStep
{
    /// <summary>The hosted services registered in the container.</summary>
    private readonly IEnumerable<IHostedService> _hostedServices;

    /// <summary>Creates the step.</summary>
    /// <param name="hostedServices">The hosted services registered in the container.</param>
    public ReferenceDataSeedStep(IEnumerable<IHostedService> hostedServices)
    {
        _hostedServices = hostedServices;
    }

    /// <inheritdoc />
    public string Name
    {
        get { return "reference-data"; }
    }

    /// <inheritdoc />
    public async Task RunAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        var seeds = _hostedServices
            .Where(service =>
            {
                return service is PlanSeedingStartupTask
                    or FirewallWhitelistSeedingStartupTask
                    or BackupDestinationSeedingStartupTask;
            })
            .ToList();

        // Three seeds exist; fewer registered means the composition lost one and the demo would start empty.
        if (seeds.Count != 3)
        {
            throw new DemoSeedException(Name, "ReferenceSeedsNotRegistered");
        }

        foreach (var seed in seeds)
        {
            await seed.StartAsync(cancellationToken);
        }
    }
}
