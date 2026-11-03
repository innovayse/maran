using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>
/// What <see cref="Maran.Modules.Accounts.Services.ResellerAllocationGuard"/> decides about the owner
/// of a plan and about one more account: counts, the disk pool, and the claim it takes on the reseller.
/// </summary>
public sealed class ResellerAllocationGuardAccountTests
{
    /// <summary>A plan owned by the account's owner is accepted.</summary>
    [Fact]
    public async Task A_plan_owned_by_the_accounts_owner_is_accepted()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope());
        var plan = fixture.AddPlan(100, fixture.Reseller.Id);

        Assert.Null(fixture.Guard.RequirePlanOwnedBy(fixture.Reseller.Id, plan));
    }

    /// <summary>An administrator's plan for an administrator's account is accepted.</summary>
    [Fact]
    public async Task An_administrators_plan_for_an_administrators_account_is_accepted()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope());
        var plan = fixture.AddPlan(100, null);

        Assert.Null(fixture.Guard.RequirePlanOwnedBy(null, plan));
    }

    /// <summary>A plan of another owner is refused as an owner mismatch, in both directions.</summary>
    [Fact]
    public async Task A_plan_of_another_owner_is_refused_as_an_owner_mismatch()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope());
        var adminPlan = fixture.AddPlan(100, null);
        var resellerPlan = fixture.AddPlan(100, fixture.Reseller.Id);

        var forReseller = fixture.Guard.RequirePlanOwnedBy(fixture.Reseller.Id, adminPlan);
        var forAdmin = fixture.Guard.RequirePlanOwnedBy(null, resellerPlan);
        var forOther = fixture.Guard.RequirePlanOwnedBy(Guid.NewGuid(), resellerPlan);

        foreach (var refusal in new[] { forReseller, forAdmin, forOther })
        {
            Assert.Equal("PlanOwnerMismatch", refusal!.Error.Code);
            Assert.Equal(ErrorType.Conflict, refusal.Error.Type);
        }
    }

    /// <summary>An account of the administrator has no envelope: it is admitted without any claim.</summary>
    [Fact]
    public async Task An_administrators_account_is_admitted_without_a_claim()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope(maxAccounts: 0));
        var plan = fixture.AddPlan(100, null);

        var refusal = await fixture.Guard.AdmitAccountAsync(null, plan, CancellationToken.None);

        Assert.Null(refusal);
        Assert.Equal(0, fixture.Reseller.AllocationRevision);
    }

    /// <summary>An account for a reseller that does not exist is refused as not found.</summary>
    [Fact]
    public async Task An_account_for_a_missing_reseller_is_refused_as_not_found()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope());
        var plan = fixture.AddPlan(100, null);

        var refusal = await fixture.Guard.AdmitAccountAsync(Guid.NewGuid(), plan, CancellationToken.None);

        Assert.Equal("ResellerNotFound", refusal!.Error.Code);
        Assert.Equal(ErrorType.NotFound, refusal.Error.Type);
    }

    /// <summary>An account that fits is admitted and the reseller is claimed.</summary>
    [Fact]
    public async Task An_account_that_fits_is_admitted_and_claims_the_reseller()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope(maxAccounts: 2));
        var plan = fixture.AddPlan(100, fixture.Reseller.Id);

        var refusal = await fixture.Guard.AdmitAccountAsync(fixture.Reseller.Id, plan, CancellationToken.None);

        Assert.Null(refusal);
        Assert.Equal(1, fixture.Reseller.AllocationRevision);
    }

    /// <summary>The last free account slot is admitted: the count limit is inclusive.</summary>
    [Fact]
    public async Task The_last_free_account_slot_is_admitted()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope(maxAccounts: 3));
        var plan = fixture.AddPlan(100, fixture.Reseller.Id);
        fixture.AddAccount(plan, fixture.Reseller.Id);
        fixture.AddAccount(plan, fixture.Reseller.Id);

        var refusal = await fixture.Guard.AdmitAccountAsync(fixture.Reseller.Id, plan, CancellationToken.None);

        Assert.Null(refusal);
    }

    /// <summary>An account over the count is refused and the reseller is not claimed.</summary>
    [Fact]
    public async Task An_account_over_the_count_is_refused_and_does_not_claim_the_reseller()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope(maxAccounts: 2));
        var plan = fixture.AddPlan(100, fixture.Reseller.Id);
        fixture.AddAccount(plan, fixture.Reseller.Id);
        fixture.AddAccount(plan, fixture.Reseller.Id);

        var refusal = await fixture.Guard.AdmitAccountAsync(fixture.Reseller.Id, plan, CancellationToken.None);

        Assert.Equal("ResellerAccountLimitReached", refusal!.Error.Code);
        Assert.Equal(ErrorType.Conflict, refusal.Error.Type);
        Assert.Equal(0, fixture.Reseller.AllocationRevision);
    }

    /// <summary>A suspended account still holds its slot.</summary>
    [Fact]
    public async Task A_suspended_account_still_holds_its_slot()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope(maxAccounts: 1));
        var plan = fixture.AddPlan(100, fixture.Reseller.Id);
        fixture.AddAccount(plan, fixture.Reseller.Id, suspended: true);

        var refusal = await fixture.Guard.AdmitAccountAsync(fixture.Reseller.Id, plan, CancellationToken.None);

        Assert.Equal("ResellerAccountLimitReached", refusal!.Error.Code);
    }

    /// <summary>Accounts of other owners do not use the reseller's slots.</summary>
    [Fact]
    public async Task Accounts_of_other_owners_do_not_use_the_resellers_slots()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope(maxAccounts: 1));
        var adminPlan = fixture.AddPlan(100, null);
        fixture.AddAccount(adminPlan, null);
        var plan = fixture.AddPlan(100, fixture.Reseller.Id);

        var refusal = await fixture.Guard.AdmitAccountAsync(fixture.Reseller.Id, plan, CancellationToken.None);

        Assert.Null(refusal);
    }

    /// <summary>A disk allocation exactly filling the pool is admitted: the pool limit is inclusive.</summary>
    [Fact]
    public async Task An_account_exactly_filling_the_disk_pool_is_admitted()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope(diskPoolMb: 300));
        var plan = fixture.AddPlan(100, fixture.Reseller.Id);
        fixture.AddAccount(plan, fixture.Reseller.Id);
        fixture.AddAccount(plan, fixture.Reseller.Id);

        var refusal = await fixture.Guard.AdmitAccountAsync(fixture.Reseller.Id, plan, CancellationToken.None);

        Assert.Null(refusal);
    }

    /// <summary>An account one megabyte over the pool is refused and the reseller is not claimed.</summary>
    [Fact]
    public async Task An_account_one_megabyte_over_the_disk_pool_is_refused()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope(diskPoolMb: 299));
        var plan = fixture.AddPlan(100, fixture.Reseller.Id);
        fixture.AddAccount(plan, fixture.Reseller.Id);
        fixture.AddAccount(plan, fixture.Reseller.Id);

        var refusal = await fixture.Guard.AdmitAccountAsync(fixture.Reseller.Id, plan, CancellationToken.None);

        Assert.Equal("ResellerDiskPoolExceeded", refusal!.Error.Code);
        Assert.Equal(ErrorType.Conflict, refusal.Error.Type);
        Assert.Equal(0, fixture.Reseller.AllocationRevision);
    }
}
