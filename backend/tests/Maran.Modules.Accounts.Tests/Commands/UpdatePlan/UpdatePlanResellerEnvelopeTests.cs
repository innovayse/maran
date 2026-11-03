using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.UpdatePlan;

/// <summary>
/// Behavioral contract of the envelope when a plan is edited: every ceiling is judged on every edit,
/// equal to a ceiling is accepted, and a raised disk quota is recomputed against the reseller's pool
/// for every account already on the plan, so editing a plan after accounts exist cannot outgrow it.
/// </summary>
public sealed class UpdatePlanResellerEnvelopeTests : IDisposable
{
    /// <summary>One mebibyte in bytes, the unit a quota is pushed to the agent in.</summary>
    private const ulong Mebibyte = 1024UL * 1024UL;

    /// <summary>A reseller with a 3000 MB pool, a 2000 MB per-plan ceiling and no runtime ceiling.</summary>
    private readonly ResellerPlanScenario _scenario = new(ResellerTestData.Envelope(
        diskPoolMb: 3_000,
        maxDiskQuotaMb: 2_000,
        maxSites: 5,
        features: AccountFeature.Mail));

    /// <inheritdoc />
    public void Dispose()
    {
        _scenario.Dispose();
    }

    /// <summary>Asserts the edit was refused with this code and left the stored plan and the host untouched.</summary>
    /// <param name="result">The handler's answer.</param>
    /// <param name="code">The code it must carry.</param>
    /// <param name="planId">The plan that was edited.</param>
    /// <param name="diskQuotaMb">The disk quota the plan must still hold.</param>
    private async Task AssertRefusedAsync(Result<PlanDto> result, string code, Guid planId, int diskQuotaMb)
    {
        Assert.Equal(code, result.Error!.Code);
        Assert.Equal(diskQuotaMb, (await _scenario.ReadPlanAsync(planId))!.DiskQuotaMb);
        Assert.DoesNotContain(_scenario.Agent.Calls, call =>
        {
            return call.StartsWith("quota:", StringComparison.Ordinal);
        });
        Assert.False(Assert.Single(_scenario.Audit.Entries).Succeeded);
        Assert.Equal(0, (await _scenario.ReadResellerAsync()).AllocationRevision);
    }

    /// <summary>A raise that brings the pool exactly to its size is accepted, and every account is pushed the new quota.</summary>
    [Fact]
    public async Task A_raise_that_brings_the_pool_exactly_to_its_size_is_accepted()
    {
        var plan = _scenario.AddPlan("Gold", _scenario.ResellerId);
        _scenario.AddAccount("olive", plan, _scenario.ResellerId);
        _scenario.AddAccount("pepper", plan, _scenario.ResellerId);

        var result = await _scenario.UpdateAsReseller().HandleAsync(
            ResellerPlanScenario.Same(plan) with { DiskQuotaMb = 1_500 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains($"quota:olive:{1_500UL * Mebibyte}", _scenario.Agent.Calls);
        Assert.Contains($"quota:pepper:{1_500UL * Mebibyte}", _scenario.Agent.Calls);
        Assert.Equal(1, (await _scenario.ReadResellerAsync()).AllocationRevision);
    }

    /// <summary>A raise that takes the pool one megabyte over is refused naming the pool and changes nothing.</summary>
    [Fact]
    public async Task A_raise_that_takes_the_pool_one_megabyte_over_is_refused_and_changes_nothing()
    {
        var plan = _scenario.AddPlan("Gold", _scenario.ResellerId);
        _scenario.AddAccount("olive", plan, _scenario.ResellerId);
        _scenario.AddAccount("pepper", plan, _scenario.ResellerId);

        var result = await _scenario.UpdateAsReseller().HandleAsync(
            ResellerPlanScenario.Same(plan) with { DiskQuotaMb = 1_501 }, CancellationToken.None);

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        await AssertRefusedAsync(result, "ResellerDiskPoolExceeded", plan.Id, 1_000);
    }

    /// <summary>The pool is recomputed for the accounts on the plan only, so a raise on a plan nobody uses fits.</summary>
    [Fact]
    public async Task A_raise_on_a_plan_nobody_uses_fits_while_other_accounts_fill_the_pool()
    {
        var used = _scenario.AddPlan("Used", _scenario.ResellerId, diskQuotaMb: 1_500);
        _scenario.AddAccount("olive", used, _scenario.ResellerId);
        _scenario.AddAccount("pepper", used, _scenario.ResellerId);
        var idle = _scenario.AddPlan("Idle", _scenario.ResellerId);

        var result = await _scenario.UpdateAsReseller().HandleAsync(
            ResellerPlanScenario.Same(idle) with { DiskQuotaMb = 2_000 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>A lowered quota needs no room in the pool, so a reseller whose envelope was lowered can get back under it.</summary>
    [Fact]
    public async Task A_lowered_quota_needs_no_room_in_the_pool()
    {
        var big = _scenario.AddPlan("Big", _scenario.ResellerId, diskQuotaMb: 2_000);
        _scenario.AddAccount("olive", big, _scenario.ResellerId);
        _scenario.AddAccount("pepper", big, _scenario.ResellerId);
        var plan = _scenario.AddPlan("Gold", _scenario.ResellerId);

        var result = await _scenario.UpdateAsReseller().HandleAsync(
            ResellerPlanScenario.Same(plan) with { DiskQuotaMb = 900 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(900, (await _scenario.ReadPlanAsync(plan.Id))!.DiskQuotaMb);
    }

    /// <summary>An edit that leaves the quota alone needs the pool for nothing and claims nothing.</summary>
    [Fact]
    public async Task An_edit_that_leaves_the_quota_alone_claims_nothing()
    {
        var plan = _scenario.AddPlan("Gold", _scenario.ResellerId);

        var result = await _scenario.UpdateAsReseller().HandleAsync(
            ResellerPlanScenario.Same(plan) with { MaxSites = 4 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, (await _scenario.ReadResellerAsync()).AllocationRevision);
    }

    /// <summary>A quota one over the per-plan ceiling is refused naming it, and exactly the ceiling is accepted.</summary>
    [Fact]
    public async Task A_quota_one_over_the_per_plan_ceiling_is_refused_and_exactly_the_ceiling_is_accepted()
    {
        var plan = _scenario.AddPlan("Gold", _scenario.ResellerId);

        var over = await _scenario.UpdateAsReseller().HandleAsync(
            ResellerPlanScenario.Same(plan) with { DiskQuotaMb = 2_001 }, CancellationToken.None);

        Assert.Equal("ResellerPlanExceedsEnvelope", over.Error!.Code);
        Assert.Equal(ErrorType.Validation, over.Error.Type);
        Assert.Equal(
            ["diskQuotaMb"], Assert.IsType<PlanEnvelopeViolationsDto>(over.Extension!.Value).Fields);

        var exact = await _scenario.UpdateAsReseller().HandleAsync(
            ResellerPlanScenario.Same(plan) with { DiskQuotaMb = 2_000 }, CancellationToken.None);

        Assert.True(exact.IsSuccess);
    }

    /// <summary>A site count, a feature and a CPU ceiling over the envelope are each named in one refusal.</summary>
    [Fact]
    public async Task A_site_count_a_feature_and_a_cpu_ceiling_over_the_envelope_are_each_named()
    {
        using var capped = new ResellerPlanScenario(ResellerTestData.Envelope(
            maxSites: 5,
            features: AccountFeature.Mail,
            runtimeLimits: RuntimeLimits.Of(
                ResourceLimit.Limited(100), ResourceLimit.Unlimited(), ResourceLimit.Unlimited())));
        var plan = capped.AddPlan("Gold", capped.ResellerId);

        var result = await capped.UpdateAsReseller().HandleAsync(
            ResellerPlanScenario.Same(plan) with
            {
                MaxSites = 6,
                Features = [AccountFeature.Mail, AccountFeature.Dns],
                CpuQuotaPercent = ResourceCeilingTestValues.Limited(101),
            },
            CancellationToken.None);

        Assert.Equal("ResellerPlanExceedsEnvelope", result.Error!.Code);
        Assert.Equal(
            ["maxSites", "features", "cpuQuotaPercent"],
            Assert.IsType<PlanEnvelopeViolationsDto>(result.Extension!.Value).Fields);
        Assert.Equal(5, (await capped.ReadPlanAsync(plan.Id))!.MaxSites);
    }

    /// <summary>An administrator's edit of a reseller's plan is held to the same ceilings.</summary>
    [Fact]
    public async Task An_administrators_edit_of_a_resellers_plan_is_held_to_the_same_ceilings()
    {
        var plan = _scenario.AddPlan("Gold", _scenario.ResellerId);

        var result = await _scenario.UpdateAsAdministrator().HandleAsync(
            ResellerPlanScenario.Same(plan) with { DiskQuotaMb = 2_001 }, CancellationToken.None);

        Assert.Equal("ResellerPlanExceedsEnvelope", result.Error!.Code);
    }

    /// <summary>An administrator's edit of its own plan is held to no envelope.</summary>
    [Fact]
    public async Task An_administrators_edit_of_its_own_plan_is_held_to_no_envelope()
    {
        var plan = _scenario.AddPlan("Mine", null);

        var result = await _scenario.UpdateAsAdministrator().HandleAsync(
            ResellerPlanScenario.Same(plan) with { DiskQuotaMb = 999_999 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>A save that loses the race for the pool is answered as the pool refusal and the host is put back.</summary>
    [Fact]
    public async Task A_save_that_loses_the_race_for_the_pool_puts_the_host_back_and_stores_nothing()
    {
        var plan = _scenario.AddPlan("Gold", _scenario.ResellerId);
        _scenario.AddAccount("olive", plan, _scenario.ResellerId);
        _scenario.SaveFailure.Next = new DbUpdateConcurrencyException("claimed elsewhere");

        var result = await _scenario.UpdateAsReseller().HandleAsync(
            ResellerPlanScenario.Same(plan) with { DiskQuotaMb = 1_500 }, CancellationToken.None);

        Assert.Equal("ResellerDiskPoolExceeded", result.Error!.Code);
        Assert.Equal(1_000, (await _scenario.ReadPlanAsync(plan.Id))!.DiskQuotaMb);
        Assert.Contains($"quota:olive:{1_500UL * Mebibyte}", _scenario.Agent.Calls);
        Assert.Equal($"quota:olive:{1_000UL * Mebibyte}", _scenario.Agent.Calls[^1]);
        Assert.False(Assert.Single(_scenario.Audit.Entries).Succeeded);
    }
}
