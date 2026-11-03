using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>
/// What <see cref="Maran.Modules.Accounts.Services.ResellerAllocationGuard"/> decides about creating a
/// plan, editing one and moving an account between plans.
/// </summary>
public sealed class ResellerAllocationGuardPlanTests
{
    /// <summary>A plan of the administrator has no envelope: it is admitted without any claim.</summary>
    [Fact]
    public async Task An_administrators_new_plan_is_admitted_without_a_claim()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope(maxPlans: 0));

        var refusal = await fixture.Guard.AdmitNewPlanAsync(null, ResellerTestData.Limits(), CancellationToken.None);

        Assert.Null(refusal);
        Assert.Equal(0, fixture.Reseller.AllocationRevision);
    }

    /// <summary>A plan for a reseller that does not exist is refused as not found.</summary>
    [Fact]
    public async Task A_new_plan_for_a_missing_reseller_is_refused_as_not_found()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope());

        var refusal = await fixture.Guard.AdmitNewPlanAsync(
            Guid.NewGuid(),
            ResellerTestData.Limits(),
            CancellationToken.None);

        Assert.Equal("ResellerNotFound", refusal!.Error.Code);
        Assert.Equal(ErrorType.NotFound, refusal.Error.Type);
    }

    /// <summary>A plan that fits is admitted and the reseller is claimed.</summary>
    [Fact]
    public async Task A_new_plan_that_fits_is_admitted_and_claims_the_reseller()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope(maxPlans: 1));

        var refusal = await fixture.Guard.AdmitNewPlanAsync(
            fixture.Reseller.Id,
            ResellerTestData.Limits(),
            CancellationToken.None);

        Assert.Null(refusal);
        Assert.Equal(1, fixture.Reseller.AllocationRevision);
    }

    /// <summary>A plan over a ceiling is refused with the offending figures named in camel case.</summary>
    [Fact]
    public async Task A_new_plan_over_a_ceiling_is_refused_naming_the_figures()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope(maxDiskQuotaMb: 500));
        var limits = ResellerTestData.Limits(diskQuotaMb: 501);

        var refusal = await fixture.Guard.AdmitNewPlanAsync(fixture.Reseller.Id, limits, CancellationToken.None);

        Assert.Equal("ResellerPlanExceedsEnvelope", refusal!.Error.Code);
        Assert.Equal(ErrorType.Validation, refusal.Error.Type);
        Assert.Equal("envelope", refusal.Extension!.Key);
        var violations = Assert.IsType<PlanEnvelopeViolationsDto>(refusal.Extension.Value);
        Assert.Equal(new[] { "diskQuotaMb" }, violations.Fields);
        Assert.Equal(0, fixture.Reseller.AllocationRevision);
    }

    /// <summary>A plan equal to the ceiling is admitted: the ceiling is inclusive.</summary>
    [Fact]
    public async Task A_new_plan_equal_to_the_ceiling_is_admitted()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope(maxDiskQuotaMb: 500));
        var limits = ResellerTestData.Limits(diskQuotaMb: 500);

        var refusal = await fixture.Guard.AdmitNewPlanAsync(fixture.Reseller.Id, limits, CancellationToken.None);

        Assert.Null(refusal);
    }

    /// <summary>One plan too many is refused and the reseller is not claimed.</summary>
    [Fact]
    public async Task A_new_plan_over_the_plan_count_is_refused_and_does_not_claim_the_reseller()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope(maxPlans: 2));
        fixture.AddPlan(100, fixture.Reseller.Id);
        fixture.AddPlan(100, fixture.Reseller.Id);

        var refusal = await fixture.Guard.AdmitNewPlanAsync(
            fixture.Reseller.Id,
            ResellerTestData.Limits(),
            CancellationToken.None);

        Assert.Equal("ResellerPlanLimitReached", refusal!.Error.Code);
        Assert.Equal(ErrorType.Conflict, refusal.Error.Type);
        Assert.Null(refusal.Extension);
        Assert.Equal(0, fixture.Reseller.AllocationRevision);
    }

    /// <summary>The last free plan slot is admitted, and plans of other owners do not use the reseller's.</summary>
    [Fact]
    public async Task The_last_free_plan_slot_is_admitted_and_other_owners_plans_do_not_count()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope(maxPlans: 2));
        fixture.AddPlan(100, fixture.Reseller.Id);
        fixture.AddPlan(100, null);
        fixture.AddPlan(100, null);

        var refusal = await fixture.Guard.AdmitNewPlanAsync(
            fixture.Reseller.Id,
            ResellerTestData.Limits(),
            CancellationToken.None);

        Assert.Null(refusal);
    }

    /// <summary>The ceiling breach is reported before the count, so the caller learns which figure to lower.</summary>
    [Fact]
    public async Task A_ceiling_breach_is_reported_before_the_plan_count()
    {
        await using var fixture = new ResellerAllocationFixture(
            ResellerTestData.Envelope(maxPlans: 0, maxDiskQuotaMb: 10));

        var refusal = await fixture.Guard.AdmitNewPlanAsync(
            fixture.Reseller.Id,
            ResellerTestData.Limits(diskQuotaMb: 11),
            CancellationToken.None);

        Assert.Equal("ResellerPlanExceedsEnvelope", refusal!.Error.Code);
    }

    /// <summary>An edit of an administrator's plan is admitted without any claim.</summary>
    [Fact]
    public async Task An_edit_of_an_administrators_plan_is_admitted_without_a_claim()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope());
        var plan = fixture.AddPlan(100, null);

        var refusal = await fixture.Guard.AdmitPlanEditAsync(
            null,
            plan,
            ResellerTestData.Limits(diskQuotaMb: 9_999_999),
            CancellationToken.None);

        Assert.Null(refusal);
        Assert.Equal(0, fixture.Reseller.AllocationRevision);
    }

    /// <summary>An edit for a missing reseller is refused as not found.</summary>
    [Fact]
    public async Task An_edit_for_a_missing_reseller_is_refused_as_not_found()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope());
        var plan = fixture.AddPlan(100, fixture.Reseller.Id);

        var refusal = await fixture.Guard.AdmitPlanEditAsync(
            Guid.NewGuid(),
            plan,
            ResellerTestData.Limits(diskQuotaMb: 100),
            CancellationToken.None);

        Assert.Equal("ResellerNotFound", refusal!.Error.Code);
    }

    /// <summary>An edit over a ceiling is refused naming the figure.</summary>
    [Fact]
    public async Task An_edit_over_a_ceiling_is_refused_naming_the_figure()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope(maxSites: 5));
        var plan = fixture.AddPlan(100, fixture.Reseller.Id);

        var refusal = await fixture.Guard.AdmitPlanEditAsync(
            fixture.Reseller.Id,
            plan,
            ResellerTestData.Limits(diskQuotaMb: 100, maxSites: 6),
            CancellationToken.None);

        Assert.Equal("ResellerPlanExceedsEnvelope", refusal!.Error.Code);
        var violations = Assert.IsType<PlanEnvelopeViolationsDto>(refusal.Extension!.Value);
        Assert.Equal(new[] { "maxSites" }, violations.Fields);
    }

    /// <summary>An edit that leaves the disk quota alone is admitted and does not claim the reseller.</summary>
    [Fact]
    public async Task An_edit_that_keeps_the_disk_quota_does_not_claim_the_reseller()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope());
        var plan = fixture.AddPlan(100, fixture.Reseller.Id);

        var refusal = await fixture.Guard.AdmitPlanEditAsync(
            fixture.Reseller.Id,
            plan,
            ResellerTestData.Limits(diskQuotaMb: 100, maxSites: 3),
            CancellationToken.None);

        Assert.Null(refusal);
        Assert.Equal(0, fixture.Reseller.AllocationRevision);
    }

    /// <summary>An edit that lowers the disk quota is admitted without the pool and claims the reseller.</summary>
    [Fact]
    public async Task An_edit_that_lowers_the_disk_quota_is_admitted_and_claims_the_reseller()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope(diskPoolMb: 1));
        var plan = fixture.AddPlan(100, fixture.Reseller.Id);
        fixture.AddAccount(plan, fixture.Reseller.Id);

        var refusal = await fixture.Guard.AdmitPlanEditAsync(
            fixture.Reseller.Id,
            plan,
            ResellerTestData.Limits(diskQuotaMb: 50),
            CancellationToken.None);

        Assert.Null(refusal);
        Assert.Equal(1, fixture.Reseller.AllocationRevision);
    }

    /// <summary>Raising a quota is judged against the pool for every account on the plan, inclusively.</summary>
    [Fact]
    public async Task Raising_a_quota_recomputes_the_pool_for_every_account_on_the_plan()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope(diskPoolMb: 300));
        var plan = fixture.AddPlan(100, fixture.Reseller.Id);
        fixture.AddAccount(plan, fixture.Reseller.Id);
        fixture.AddAccount(plan, fixture.Reseller.Id);

        var atThePool = await fixture.Guard.AdmitPlanEditAsync(
            fixture.Reseller.Id,
            plan,
            ResellerTestData.Limits(diskQuotaMb: 150),
            CancellationToken.None);
        var overThePool = await fixture.Guard.AdmitPlanEditAsync(
            fixture.Reseller.Id,
            plan,
            ResellerTestData.Limits(diskQuotaMb: 151),
            CancellationToken.None);

        Assert.Null(atThePool);
        Assert.Equal("ResellerDiskPoolExceeded", overThePool!.Error.Code);
        Assert.Equal(ErrorType.Conflict, overThePool.Error.Type);
    }

    /// <summary>A refused raise does not claim the reseller.</summary>
    [Fact]
    public async Task A_refused_raise_does_not_claim_the_reseller()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope(diskPoolMb: 100));
        var plan = fixture.AddPlan(100, fixture.Reseller.Id);
        fixture.AddAccount(plan, fixture.Reseller.Id);

        await fixture.Guard.AdmitPlanEditAsync(
            fixture.Reseller.Id,
            plan,
            ResellerTestData.Limits(diskQuotaMb: 101),
            CancellationToken.None);

        Assert.Equal(0, fixture.Reseller.AllocationRevision);
    }

    /// <summary>A plan change of an administrator's account is admitted without any claim.</summary>
    [Fact]
    public async Task A_plan_change_of_an_administrators_account_is_admitted_without_a_claim()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope());
        var current = fixture.AddPlan(100, null);
        var target = fixture.AddPlan(900, null);

        var refusal = await fixture.Guard.AdmitPlanChangeAsync(null, current, target, CancellationToken.None);

        Assert.Null(refusal);
        Assert.Equal(0, fixture.Reseller.AllocationRevision);
    }

    /// <summary>A plan change for a missing reseller is refused as not found.</summary>
    [Fact]
    public async Task A_plan_change_for_a_missing_reseller_is_refused_as_not_found()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope());
        var current = fixture.AddPlan(100, fixture.Reseller.Id);
        var target = fixture.AddPlan(200, fixture.Reseller.Id);

        var refusal = await fixture.Guard.AdmitPlanChangeAsync(
            Guid.NewGuid(),
            current,
            target,
            CancellationToken.None);

        Assert.Equal("ResellerNotFound", refusal!.Error.Code);
    }

    /// <summary>A move to a plan of the same quota is admitted and does not claim the reseller.</summary>
    [Fact]
    public async Task A_move_to_a_plan_of_the_same_quota_does_not_claim_the_reseller()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope());
        var current = fixture.AddPlan(100, fixture.Reseller.Id);
        var target = fixture.AddPlan(100, fixture.Reseller.Id);

        var refusal = await fixture.Guard.AdmitPlanChangeAsync(
            fixture.Reseller.Id,
            current,
            target,
            CancellationToken.None);

        Assert.Null(refusal);
        Assert.Equal(0, fixture.Reseller.AllocationRevision);
    }

    /// <summary>A move to a smaller plan is admitted even over the pool and claims the reseller.</summary>
    [Fact]
    public async Task A_move_to_a_smaller_plan_is_admitted_even_over_the_pool_and_claims_the_reseller()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope(diskPoolMb: 1));
        var current = fixture.AddPlan(500, fixture.Reseller.Id);
        var target = fixture.AddPlan(100, fixture.Reseller.Id);
        fixture.AddAccount(current, fixture.Reseller.Id);

        var refusal = await fixture.Guard.AdmitPlanChangeAsync(
            fixture.Reseller.Id,
            current,
            target,
            CancellationToken.None);

        Assert.Null(refusal);
        Assert.Equal(1, fixture.Reseller.AllocationRevision);
    }

    /// <summary>A move to a larger plan is judged against the pool, inclusively.</summary>
    [Fact]
    public async Task A_move_to_a_larger_plan_is_judged_against_the_pool_inclusively()
    {
        await using var fixture = new ResellerAllocationFixture(ResellerTestData.Envelope(diskPoolMb: 250));
        var current = fixture.AddPlan(100, fixture.Reseller.Id);
        var fits = fixture.AddPlan(150, fixture.Reseller.Id);
        var over = fixture.AddPlan(151, fixture.Reseller.Id);
        fixture.AddAccount(current, fixture.Reseller.Id);
        fixture.AddAccount(current, fixture.Reseller.Id);

        var atThePool = await fixture.Guard.AdmitPlanChangeAsync(
            fixture.Reseller.Id,
            current,
            fits,
            CancellationToken.None);
        var overThePool = await fixture.Guard.AdmitPlanChangeAsync(
            fixture.Reseller.Id,
            current,
            over,
            CancellationToken.None);

        Assert.Null(atThePool);
        Assert.Equal("ResellerDiskPoolExceeded", overThePool!.Error.Code);
        Assert.Equal(ErrorType.Conflict, overThePool.Error.Type);
    }
}
