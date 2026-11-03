using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Commands.UpdatePlan;

/// <summary>
/// Behavioral contract of plan edits by a suspended reseller: refused before the plan is read, and
/// nothing is changed on the row or on the host.
/// </summary>
public sealed class UpdatePlanSuspendedResellerTests : IDisposable
{
    /// <summary>A reseller stored already suspended.</summary>
    private readonly ResellerPlanScenario _scenario = new(suspended: true);

    /// <inheritdoc />
    public void Dispose()
    {
        _scenario.Dispose();
    }

    /// <summary>A suspended reseller's edit is refused as a conflict and changes nothing.</summary>
    [Fact]
    public async Task A_suspended_resellers_edit_is_refused_and_changes_nothing()
    {
        var plan = _scenario.AddPlan("Gold", _scenario.ResellerId);

        var result = await _scenario.UpdateAsReseller().HandleAsync(
            ResellerPlanScenario.Same(plan) with { MaxSites = 4, DiskQuotaMb = 2_000 }, CancellationToken.None);

        Assert.Equal("ResellerSuspended", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        var stored = await _scenario.ReadPlanAsync(plan.Id);
        Assert.Equal(5, stored!.MaxSites);
        Assert.Equal(1_000, stored.DiskQuotaMb);
        Assert.Empty(_scenario.Agent.Calls);
        var entry = Assert.Single(_scenario.Audit.Entries);
        Assert.Equal(AccountAuditActions.PlanUpdated, entry.Action);
        Assert.False(entry.Succeeded);
    }
}
