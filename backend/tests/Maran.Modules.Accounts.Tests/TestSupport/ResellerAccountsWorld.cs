using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// One in-memory database holding two resellers, each with a plan and an account, and the
/// administrator's own plan, so a test can act as any caller against the same rows. Every row is
/// seeded through an administrator's context, which no query filter narrows.
/// </summary>
public sealed class ResellerAccountsWorld : IDisposable
{
    /// <summary>The name of the in-memory database every context of this world opens.</summary>
    private readonly string _database = Guid.NewGuid().ToString();

    /// <summary>The contexts handed out, disposed with the world.</summary>
    private readonly List<AccountsDbContext> _contexts = [];

    /// <summary>The first reseller.</summary>
    public Reseller ResellerA { get; }

    /// <summary>The second reseller.</summary>
    public Reseller ResellerB { get; }

    /// <summary>A plan owned by <see cref="ResellerA"/>, selling 1,000 MB.</summary>
    public Plan PlanA { get; }

    /// <summary>A second plan owned by <see cref="ResellerA"/>, selling 2,000 MB.</summary>
    public Plan LargePlanA { get; }

    /// <summary>A plan owned by <see cref="ResellerB"/>.</summary>
    public Plan PlanB { get; }

    /// <summary>A plan owned by the administrator, selling 1,000 MB.</summary>
    public Plan AdminPlan { get; }

    /// <summary>An account owned by <see cref="ResellerA"/>, on <see cref="PlanA"/>.</summary>
    public Account AccountA { get; }

    /// <summary>An account owned by <see cref="ResellerB"/>, on <see cref="PlanB"/>.</summary>
    public Account AccountB { get; }

    /// <summary>The login of <see cref="ResellerA"/>.</summary>
    public FakeCurrentUser UserA { get; }

    /// <summary>The login of <see cref="ResellerB"/>.</summary>
    public FakeCurrentUser UserB { get; }

    /// <summary>Seeds the world.</summary>
    /// <param name="envelopeA">What <see cref="ResellerA"/> was sold; a generous envelope when not stated.</param>
    public ResellerAccountsWorld(ResellerEnvelope? envelopeA = null)
    {
        ResellerA = ResellerTestData.Reseller("alpha", envelopeA);
        ResellerB = ResellerTestData.Reseller("bravo");
        UserA = FakeCurrentUser.Reseller(ResellerA.Id, "alpha-login");
        UserB = FakeCurrentUser.Reseller(ResellerB.Id, "bravo-login");
        PlanA = Plan.Define(Guid.NewGuid(), "AlphaPlan", 1_000, 5, 5, 5, 5, 5, 5, resellerId: ResellerA.Id);
        LargePlanA = Plan.Define(Guid.NewGuid(), "AlphaLarge", 2_000, 5, 5, 5, 5, 5, 5, resellerId: ResellerA.Id);
        PlanB = Plan.Define(Guid.NewGuid(), "BravoPlan", 1_000, 5, 5, 5, 5, 5, 5, resellerId: ResellerB.Id);
        AdminPlan = Plan.Define(Guid.NewGuid(), "AdminPlan", 1_000, 5, 5, 5, 5, 5, 5);
        AccountA = new Account(
            Guid.NewGuid(), "alpha-one", "alpha-one.example.com", PlanA.Id, ResellerTestData.CreatedAt,
            "alpha@example.com", resellerId: ResellerA.Id);
        AccountB = new Account(
            Guid.NewGuid(), "bravo-one", "bravo-one.example.com", PlanB.Id, ResellerTestData.CreatedAt,
            "bravo@example.com", resellerId: ResellerB.Id);

        var seeder = ContextFor(FakeCurrentUser.Admin());
        seeder.Resellers.AddRange(ResellerA, ResellerB);
        seeder.Plans.AddRange(PlanA, LargePlanA, PlanB, AdminPlan);
        seeder.Accounts.AddRange(AccountA, AccountB);
        seeder.SaveChanges();
    }

    /// <summary>Opens a context over this world's rows, filtered for <paramref name="currentUser"/>.</summary>
    /// <param name="currentUser">The caller the context acts for.</param>
    /// <param name="interceptors">Interceptors to attach, for a test that arranges a failed save.</param>
    /// <returns>The context, disposed with the world.</returns>
    public AccountsDbContext ContextFor(ICurrentUser currentUser, params IInterceptor[] interceptors)
    {
        var context = AccountsTestContext.CreateOn(_database, currentUser, interceptors);
        _contexts.Add(context);
        return context;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var context in _contexts)
        {
            context.Dispose();
        }
    }
}
