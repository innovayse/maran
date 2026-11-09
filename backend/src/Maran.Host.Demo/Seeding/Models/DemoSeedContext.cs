using Maran.Host.Demo.World;
using Maran.SharedKernel.Callers;

namespace Maran.Host.Demo.Seeding.Models;

/// <summary>
/// What the steps share while one seed runs: the world being built and the ids earlier steps produced
/// for later ones. A run builds one and discards it; nothing outlives the command.
/// </summary>
public sealed class DemoSeedContext
{
    /// <summary>The world being built.</summary>
    public DemoWorld World { get; }

    /// <summary>The administrator the seed acts as once the administrator step has created it; null before.</summary>
    public AdministratorCaller? Administrator { get; set; }

    /// <summary>Plan ids by the name accounts refer to a plan by.</summary>
    public Dictionary<string, Guid> PlanIds { get; } = new(StringComparer.Ordinal);

    /// <summary>Reseller ids by reseller name.</summary>
    public Dictionary<string, Guid> ResellerIds { get; } = new(StringComparer.Ordinal);

    /// <summary>The login ids of the resellers, by reseller name.</summary>
    public Dictionary<string, Guid> ResellerUserIds { get; } = new(StringComparer.Ordinal);

    /// <summary>Plan ids owned by a reseller, by reseller name and then plan name.</summary>
    public Dictionary<string, Dictionary<string, Guid>> ResellerPlanIds { get; } = new(StringComparer.Ordinal);

    /// <summary>Account ids by account name.</summary>
    public Dictionary<string, Guid> AccountIds { get; } = new(StringComparer.Ordinal);

    /// <summary>Site ids by domain.</summary>
    public Dictionary<string, Guid> SiteIds { get; } = new(StringComparer.Ordinal);

    /// <summary>Creates the context.</summary>
    /// <param name="world">The world being built.</param>
    public DemoSeedContext(DemoWorld world)
    {
        World = world;
    }

    /// <summary>The id of the plan an account is created on.</summary>
    /// <param name="account">The account.</param>
    /// <returns>
    /// The reseller's own plan for a reseller account, the administrator's or a standard plan otherwise.
    /// </returns>
    public Guid PlanFor(DemoAccountDefinition account)
    {
        return account.ResellerName is { } reseller
            ? ResellerPlanIds[reseller][account.PlanName]
            : PlanIds[account.PlanName];
    }

    /// <summary>The id of an account the accounts step created.</summary>
    /// <param name="name">The account name.</param>
    /// <returns>The account id.</returns>
    public Guid AccountId(string name)
    {
        return AccountIds[name];
    }
}
