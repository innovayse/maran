using Maran.Host.Demo.Seeding.Interfaces;
using Maran.Host.Demo.Seeding.Models;
using Maran.Host.Demo.Seeding.Services;
using Maran.Modules.Accounts.Common;

namespace Maran.Host.Demo.Seeding.Steps;

/// <summary>
/// Creates each reseller's plans acting as that reseller, because a plan belongs to whoever created it
/// and an account of a reseller may only use the reseller's own plans.
/// </summary>
public sealed class ResellerPlanSeedStep : IDemoSeedStep
{
    /// <summary>Runs commands through the real handlers.</summary>
    private readonly DemoSeedCommandRunner _runner;

    /// <summary>Switches the caller the seed acts as.</summary>
    private readonly DemoSeedCallerAccessor _caller;

    /// <summary>Creates the step.</summary>
    /// <param name="runner">Runs commands through the real handlers.</param>
    /// <param name="caller">Switches the caller the seed acts as.</param>
    public ResellerPlanSeedStep(DemoSeedCommandRunner runner, DemoSeedCallerAccessor caller)
    {
        _runner = runner;
        _caller = caller;
    }

    /// <inheritdoc />
    public string Name
    {
        get { return "reseller-plans"; }
    }

    /// <inheritdoc />
    public async Task RunAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        foreach (var reseller in context.World.Resellers)
        {
            using var acting = _caller.Act(ResellerSeedStep.CallerFor(context, reseller.Name));

            var plans = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (var plan in reseller.Plans)
            {
                var created = await _runner.RunAsync<PlanDto>(
                    Name,
                    plan.ToCommand(DemoSeedCommandRunner.Origin, DemoSeedCommandRunner.Agent),
                    cancellationToken);
                plans[plan.Name] = created.Id;
            }

            context.ResellerPlanIds[reseller.Name] = plans;
        }
    }
}
