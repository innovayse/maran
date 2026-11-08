using Maran.Host.Demo.Seeding.Exceptions;
using Maran.Host.Demo.Seeding.Interfaces;
using Maran.Host.Demo.Seeding.Models;
using Maran.Host.Demo.Seeding.Services;
using Maran.Host.Demo.World;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Seeders;

namespace Maran.Host.Demo.Seeding.Steps;

/// <summary>
/// Registers the three standard plans under the names accounts refer to them by, and adds the
/// administrator's own plans of the world.
/// </summary>
public sealed class PlanSeedStep : IDemoSeedStep
{
    /// <summary>Runs commands through the real handlers.</summary>
    private readonly DemoSeedCommandRunner _runner;

    /// <summary>Creates the step.</summary>
    /// <param name="runner">Runs commands through the real handlers.</param>
    public PlanSeedStep(DemoSeedCommandRunner runner)
    {
        _runner = runner;
    }

    /// <inheritdoc />
    public string Name
    {
        get { return "plans"; }
    }

    /// <inheritdoc />
    public async Task RunAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        // The ids the plan seeder fixes; the reference-data step created the rows, and a missing
        // row would surface at the first account as PlanNotFound, naming the account.
        context.PlanIds[StandardDemoWorld.StarterPlanName] = PlanSeeder.StarterPlanId;
        context.PlanIds[StandardDemoWorld.BusinessPlanName] = PlanSeeder.BusinessPlanId;
        context.PlanIds[StandardDemoWorld.UnlimitedPlanName] = PlanSeeder.UnlimitedPlanId;

        foreach (var plan in context.World.Plans)
        {
            if (context.PlanIds.ContainsKey(plan.Name))
            {
                throw new DemoSeedException(Name, "PlanNameRepeated");
            }

            var created = await _runner.RunAsync<PlanDto>(
                Name,
                plan.ToCommand(DemoSeedCommandRunner.Origin, DemoSeedCommandRunner.Agent),
                cancellationToken);
            context.PlanIds[plan.Name] = created.Id;
        }
    }
}
