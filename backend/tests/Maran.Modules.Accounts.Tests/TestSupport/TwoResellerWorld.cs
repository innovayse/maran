using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// One in-memory database holding two resellers, each owning a plan and an account on it, and an
/// administrator-owned plan and account, so a query test can read the same rows as an administrator
/// and as either reseller and show that each caller sees what it owns and nothing else.
/// </summary>
public sealed class TwoResellerWorld
{
    /// <summary>The instant every account in the world was created.</summary>
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The name of the in-memory database every context of this world opens.</summary>
    private readonly string _databaseName = Guid.NewGuid().ToString();

    /// <summary>The first reseller.</summary>
    public Reseller Alpha { get; private set; } = null!;

    /// <summary>The second reseller.</summary>
    public Reseller Beta { get; private set; } = null!;

    /// <summary>The plan the first reseller owns.</summary>
    public Plan AlphaPlan { get; private set; } = null!;

    /// <summary>The plan the second reseller owns.</summary>
    public Plan BetaPlan { get; private set; } = null!;

    /// <summary>The plan the administrator owns.</summary>
    public Plan AdminPlan { get; private set; } = null!;

    /// <summary>The account on <see cref="AlphaPlan"/>, owned by the first reseller.</summary>
    public Account AlphaAccount { get; private set; } = null!;

    /// <summary>The account on <see cref="BetaPlan"/>, owned by the second reseller.</summary>
    public Account BetaAccount { get; private set; } = null!;

    /// <summary>The account on <see cref="AdminPlan"/>, owned by the administrator.</summary>
    public Account AdminAccount { get; private set; } = null!;

    /// <summary>Builds the world and writes it to its database as an administrator.</summary>
    /// <returns>The seeded world.</returns>
    public static async Task<TwoResellerWorld> SeedAsync()
    {
        var world = new TwoResellerWorld
        {
            Alpha = ResellerTestData.Reseller("alpha"),
            Beta = ResellerTestData.Reseller("beta"),
        };
        world.AlphaPlan = Plan.Define(
            Guid.NewGuid(), "Alpha plan", 1_000, 5, 5, 5, 5, 5, 5, resellerId: world.Alpha.Id);
        world.BetaPlan = Plan.Define(
            Guid.NewGuid(), "Beta plan", 2_000, 5, 5, 5, 5, 5, 5, resellerId: world.Beta.Id);
        world.AdminPlan = Plan.Define(Guid.NewGuid(), "Admin plan", 3_000, 5, 5, 5, 5, 5, 5);
        world.AlphaAccount = new Account(
            Guid.NewGuid(),
            "alphaacct",
            "alphaacct.example.com",
            world.AlphaPlan.Id,
            Created,
            resellerId: world.Alpha.Id);
        world.BetaAccount = new Account(
            Guid.NewGuid(),
            "betaacct",
            "betaacct.example.com",
            world.BetaPlan.Id,
            Created,
            resellerId: world.Beta.Id);
        world.AdminAccount = new Account(
            Guid.NewGuid(), "adminacct", "adminacct.example.com", world.AdminPlan.Id, Created);

        await using var seeding = world.ContextFor(FakeCurrentUser.Admin());
        seeding.Resellers.AddRange(world.Alpha, world.Beta);
        seeding.Plans.AddRange(world.AlphaPlan, world.BetaPlan, world.AdminPlan);
        seeding.Accounts.AddRange(world.AlphaAccount, world.BetaAccount, world.AdminAccount);
        await seeding.SaveChangesAsync();
        return world;
    }

    /// <summary>Opens a context over the world's rows whose filters are evaluated for a caller.</summary>
    /// <param name="currentUser">The caller the context's query filters close over.</param>
    /// <returns>The context; the caller disposes it.</returns>
    public AccountsDbContext ContextFor(ICurrentUser currentUser)
    {
        return AccountsTestContext.CreateOn(_databaseName, currentUser);
    }

    /// <summary>The first reseller as a signed-in caller.</summary>
    /// <returns>A reseller principal acting for <see cref="Alpha"/>.</returns>
    public FakeCurrentUser AsAlpha()
    {
        return FakeCurrentUser.Reseller(Alpha.Id, "alpha");
    }

    /// <summary>The second reseller as a signed-in caller.</summary>
    /// <returns>A reseller principal acting for <see cref="Beta"/>.</returns>
    public FakeCurrentUser AsBeta()
    {
        return FakeCurrentUser.Reseller(Beta.Id, "beta");
    }
}
