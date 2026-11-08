using Maran.Host.Demo.Guard;
using Maran.Host.Demo.World;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Callers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Maran.Host.Demo.Seeding.Services;

/// <summary>
/// Reads the demonstration's identity-bearing resources from the database and marks them in
/// <see cref="DemoProtectedResources"/>: the published administrator, the featured customer's account
/// and plan, every reseller of the world, and the plans and accounts those resellers own. The guard
/// refuses to suspend, delete, transfer or re-plan whatever is in that set.
/// </summary>
/// <remarks>
/// The set lives in memory, and the process that seeds is not the process that serves, so BOTH call
/// this: the seeder when it has finished, and the API at start (<see cref="DemoProtectedResourceStartup"/>).
/// One rule in one place means the two cannot disagree about what is protected. A database that holds
/// no demonstration yet protects nothing and says so by returning zero and leaving the set not loaded, which
/// the guard answers by refusing everything that depends on it.
/// </remarks>
public sealed class DemoProtectedResourceLoader
{
    /// <summary>Opens the scope the contexts are read in.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Lets the read act as the administrator, whose view the tenant filters do not narrow.</summary>
    private readonly DemoSeedCallerAccessor _caller;

    /// <summary>The set that is filled.</summary>
    private readonly DemoProtectedResources _protected;

    /// <summary>The published sign-ins, for the administrator's name.</summary>
    private readonly DemoIdentityOptions _identities;

    /// <summary>Creates the loader.</summary>
    /// <param name="scopeFactory">Opens the scope the contexts are read in.</param>
    /// <param name="caller">Lets the read act as the administrator.</param>
    /// <param name="protectedResources">The set that is filled.</param>
    /// <param name="identities">The published sign-ins.</param>
    public DemoProtectedResourceLoader(
        IServiceScopeFactory scopeFactory,
        DemoSeedCallerAccessor caller,
        DemoProtectedResources protectedResources,
        IOptions<DemoIdentityOptions> identities)
    {
        _scopeFactory = scopeFactory;
        _caller = caller;
        _protected = protectedResources;
        _identities = identities.Value;
    }

    /// <summary>Marks every identity-bearing resource of the world that the database holds.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>How many resources were marked; zero when the database holds no demonstration.</returns>
    public async Task<int> LoadAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var administrator = await services.GetRequiredService<IdentityDbContext>().Users
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Username == _identities.AdministratorUsername, cancellationToken);
        if (administrator is null)
        {
            return 0;
        }

        using var acting = _caller.Act(new AdministratorCaller(administrator.Id, administrator.Username));

        var world = StandardDemoWorld.Instance;
        var accounts = services.GetRequiredService<AccountsDbContext>();

        // Named before the query: a statement-bodied lambda cannot become part of an expression tree.
        var resellerNames = world.Resellers.Select(definition =>
        {
            return definition.Name;
        }).ToList();
        var resellerIds = await accounts.Resellers
            .AsNoTracking()
            .Where(reseller => resellerNames.Contains(reseller.Name))
            .Select(reseller => reseller.Id)
            .ToListAsync(cancellationToken);
        var protectedAccounts = await accounts.Accounts
            .AsNoTracking()
            .Where(account => account.Name == world.FeaturedCustomerAccountName
                || (account.ResellerId != null && resellerIds.Contains(account.ResellerId.Value)))
            .Select(account => new { account.Id, account.PlanId })
            .ToListAsync(cancellationToken);
        var resellerPlanIds = await accounts.Plans
            .AsNoTracking()
            .Where(plan => plan.ResellerId != null && resellerIds.Contains(plan.ResellerId.Value))
            .Select(plan => plan.Id)
            .ToListAsync(cancellationToken);

        var ids = new HashSet<Guid>(resellerIds.Count + resellerPlanIds.Count + (protectedAccounts.Count * 2) + 1)
        {
            administrator.Id,
        };
        ids.UnionWith(resellerIds);
        ids.UnionWith(resellerPlanIds);
        foreach (var account in protectedAccounts)
        {
            ids.Add(account.Id);
            ids.Add(account.PlanId);
        }

        foreach (var id in ids)
        {
            _protected.Protect(id);
        }

        _protected.MarkLoaded();

        return ids.Count;
    }
}
