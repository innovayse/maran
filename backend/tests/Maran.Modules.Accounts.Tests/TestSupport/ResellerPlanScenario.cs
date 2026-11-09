using Maran.Modules.Accounts.Commands.CreatePlan;
using Maran.Modules.Accounts.Commands.DeletePlan;
using Maran.Modules.Accounts.Commands.UpdatePlan;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// One in-memory Accounts database seen by two contexts at once: an administrator's, which writes the
/// fixtures and reads the outcome, and a reseller's, which the handler under test runs against and
/// whose query filters therefore narrow it. It is the arrangement every "a reseller manages its own
/// plans" test needs, written once.
/// </summary>
public sealed class ResellerPlanScenario : IDisposable
{
    /// <summary>The instant every account built here was created.</summary>
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The reseller the scenario acts as.</summary>
    public Guid ResellerId { get; }

    /// <summary>The reseller login the handlers under test run as.</summary>
    public FakeCurrentUser ResellerUser { get; }

    /// <summary>The administrator's context: it is not narrowed, and it writes the fixtures.</summary>
    public AccountsDbContext Seed { get; }

    /// <summary>The reseller's context: its query filters hide every row another owner holds.</summary>
    public AccountsDbContext Own { get; }

    /// <summary>Everything the handlers journal.</summary>
    public RecordingAuditWriter Audit { get; } = new();

    /// <summary>Throws an arranged exception on the next save of the reseller's context.</summary>
    public ArmedSaveFailureInterceptor SaveFailure { get; } = new();

    /// <summary>The agent double that owns quotas and runtime ceilings.</summary>
    public RecordingAgentAccountsClient Agent { get; } = new();

    /// <summary>The display names of the plans the panel ships.</summary>
    public StubDisplayNames DisplayNames { get; } = new StubDisplayNames().With("PlanStarterName", "Starter");

    /// <summary>Stores the reseller and opens both contexts.</summary>
    /// <param name="envelope">What the reseller was sold; a generous envelope when not stated.</param>
    /// <param name="suspended">Whether the reseller is stored already suspended.</param>
    public ResellerPlanScenario(ResellerEnvelope? envelope = null, bool suspended = false)
    {
        var database = Guid.NewGuid().ToString();
        var reseller = ResellerTestData.Reseller("acme", envelope);
        if (suspended)
        {
            reseller.Suspend();
        }

        ResellerId = reseller.Id;
        ResellerUser = FakeCurrentUser.Reseller(ResellerId);
        Seed = AccountsTestContext.CreateOn(database, FakeCurrentUser.Admin());
        Own = AccountsTestContext.CreateOn(database, ResellerUser, SaveFailure);
        Seed.Resellers.Add(reseller);
        Seed.SaveChanges();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Own.Dispose();
        Seed.Dispose();
    }

    /// <summary>Stores a second reseller, the rival whose rows the first must never see.</summary>
    /// <returns>The rival's id.</returns>
    public Guid AddRival()
    {
        var rival = ResellerTestData.Reseller("rival");
        Seed.Resellers.Add(rival);
        Seed.SaveChanges();
        return rival.Id;
    }

    /// <summary>Stores a plan.</summary>
    /// <param name="name">The plan's name.</param>
    /// <param name="owner">The owning reseller, or null for the administrator's.</param>
    /// <param name="diskQuotaMb">The plan's disk quota.</param>
    /// <returns>The stored plan.</returns>
    public Plan AddPlan(string name, Guid? owner, int diskQuotaMb = 1_000)
    {
        var plan = Plan.Define(
            Guid.NewGuid(), name, diskQuotaMb, 5, 5, 5, 5, 5, 5, AccountFeature.None, owner);
        Seed.Plans.Add(plan);
        Seed.SaveChanges();
        return plan;
    }

    /// <summary>Stores an account on a plan.</summary>
    /// <param name="name">The account's name.</param>
    /// <param name="plan">The plan it is on.</param>
    /// <param name="owner">The owning reseller, or null for the administrator's.</param>
    /// <returns>The stored account.</returns>
    public Account AddAccount(string name, Plan plan, Guid? owner)
    {
        var account = new Account(
            Guid.NewGuid(), name, $"{name}.example.com", plan.Id, Created, resellerId: owner);
        Seed.Accounts.Add(account);
        Seed.SaveChanges();
        return account;
    }

    /// <summary>Re-reads a plan through the administrator's context, bypassing every tracked copy.</summary>
    /// <param name="planId">The plan to read.</param>
    /// <returns>The stored plan, or null.</returns>
    public async Task<Plan?> ReadPlanAsync(Guid planId)
    {
        return await Seed.Plans.AsNoTracking().SingleOrDefaultAsync(plan => plan.Id == planId);
    }

    /// <summary>Re-reads the reseller through the administrator's context.</summary>
    /// <returns>The stored reseller.</returns>
    public async Task<Reseller> ReadResellerAsync()
    {
        return await Seed.Resellers.AsNoTracking().SingleAsync(reseller => reseller.Id == ResellerId);
    }

    /// <summary>Builds the creation handler running as the reseller.</summary>
    /// <returns>The handler.</returns>
    public CreatePlanCommandHandler CreateAsReseller()
    {
        return PlanHandlerFactory.Create(Own, ResellerUser, Audit, DisplayNames);
    }

    /// <summary>Builds the creation handler running as an administrator.</summary>
    /// <returns>The handler.</returns>
    public CreatePlanCommandHandler CreateAsAdministrator()
    {
        return PlanHandlerFactory.Create(Seed, FakeCurrentUser.Admin(), Audit, DisplayNames);
    }

    /// <summary>Builds the edit handler running as the reseller.</summary>
    /// <returns>The handler.</returns>
    public UpdatePlanCommandHandler UpdateAsReseller()
    {
        return PlanHandlerFactory.Update(Own, ResellerUser, Audit, DisplayNames, Agent);
    }

    /// <summary>Builds the edit handler running as an administrator.</summary>
    /// <returns>The handler.</returns>
    public UpdatePlanCommandHandler UpdateAsAdministrator()
    {
        return PlanHandlerFactory.Update(Seed, FakeCurrentUser.Admin(), Audit, DisplayNames, Agent);
    }

    /// <summary>Builds the deletion handler running as the reseller.</summary>
    /// <returns>The handler.</returns>
    public DeletePlanCommandHandler DeleteAsReseller()
    {
        return PlanHandlerFactory.Delete(Own, ResellerUser, Audit);
    }

    /// <summary>A creation request that fits a generous envelope; a test varies one figure with <c>with</c>.</summary>
    /// <param name="name">The plan's name.</param>
    /// <returns>The command.</returns>
    public static CreatePlanCommand Create(string name = "Gold")
    {
        return new CreatePlanCommand(
            name, 1_000, 5, 5, 5, 5, 5, 5,
            ResourceCeilingTestValues.Unlimited,
            ResourceCeilingTestValues.Unlimited,
            ResourceCeilingTestValues.Unlimited,
            [],
            IpAddress: "203.0.113.7",
            UserAgent: "unit-tests");
    }

    /// <summary>An edit of <paramref name="plan"/> that changes nothing; a test varies one figure with <c>with</c>.</summary>
    /// <param name="plan">The plan to edit, as it was stored by <see cref="AddPlan"/>.</param>
    /// <returns>The command.</returns>
    public static UpdatePlanCommand Same(Plan plan)
    {
        return new UpdatePlanCommand(
            plan.Name ?? throw new InvalidOperationException("The plan under edit carries no operator-written name."),
            plan.DiskQuotaMb, 5, 5, 5, 5, 5, 5,
            ResourceCeilingTestValues.Unlimited,
            ResourceCeilingTestValues.Unlimited,
            ResourceCeilingTestValues.Unlimited,
            [],
            PlanId: plan.Id,
            IpAddress: "203.0.113.7",
            UserAgent: "unit-tests");
    }
}
