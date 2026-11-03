using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// An administrator's in-memory Accounts context seeded with one reseller, plus the means to give it
/// plans and accounts, so a test of what a reseller owns states only the figures it is about.
/// </summary>
public sealed class ResellerAllocationFixture : IAsyncDisposable
{
    /// <summary>The context every seeded row lives in; an administrator's, which the filters do not narrow.</summary>
    public AccountsDbContext DbContext { get; }

    /// <summary>The seeded reseller, tracked by <see cref="DbContext"/>.</summary>
    public Reseller Reseller { get; }

    /// <summary>The guard under test, over <see cref="DbContext"/>.</summary>
    public ResellerAllocationGuard Guard { get; }

    /// <summary>Creates the fixture and seeds the reseller.</summary>
    /// <param name="envelope">What the reseller was sold.</param>
    public ResellerAllocationFixture(ResellerEnvelope envelope)
    {
        DbContext = AccountsTestContext.Create();
        Reseller = ResellerTestData.Reseller(envelope: envelope);
        DbContext.Resellers.Add(Reseller);
        DbContext.SaveChanges();
        Guard = new ResellerAllocationGuard(DbContext);
    }

    /// <summary>Saves a plan of the given disk quota, owned by <paramref name="ownerId"/>.</summary>
    /// <param name="diskQuotaMb">The plan's disk quota.</param>
    /// <param name="ownerId">The owning reseller, or null for the administrator.</param>
    /// <returns>The saved plan.</returns>
    public Plan AddPlan(int diskQuotaMb, Guid? ownerId)
    {
        var plan = Plan.Define(
            Guid.NewGuid(),
            $"plan-{Guid.NewGuid():N}",
            diskQuotaMb,
            5,
            5,
            5,
            5,
            5,
            5,
            resellerId: ownerId);
        DbContext.Plans.Add(plan);
        DbContext.SaveChanges();
        return plan;
    }

    /// <summary>Saves an account on a plan, owned by <paramref name="ownerId"/>.</summary>
    /// <param name="plan">The plan the account is on.</param>
    /// <param name="ownerId">The owning reseller, or null for the administrator.</param>
    /// <param name="suspended">Whether the account is suspended.</param>
    /// <returns>The saved account.</returns>
    public Account AddAccount(Plan plan, Guid? ownerId, bool suspended = false)
    {
        var name = $"acc{Guid.NewGuid():N}"[..16];
        var account = new Account(
            Guid.NewGuid(),
            name,
            $"{name}.example.com",
            plan.Id,
            ResellerTestData.CreatedAt,
            resellerId: ownerId);
        if (suspended)
        {
            account.Suspend();
        }

        DbContext.Accounts.Add(account);
        DbContext.SaveChanges();
        return account;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();
    }
}
