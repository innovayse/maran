using Maran.Modules.Accounts.Commands.DeletePlan;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Commands.DeletePlan;

/// <summary>
/// Behavioral contract of plan deletion by a suspended reseller: its live token cannot write, so the
/// plan survives and the refusal is journalled.
/// </summary>
public sealed class DeletePlanSuspendedResellerTests : IDisposable
{
    /// <summary>A reseller stored already suspended.</summary>
    private readonly ResellerPlanScenario _scenario = new(suspended: true);

    /// <inheritdoc />
    public void Dispose()
    {
        _scenario.Dispose();
    }

    /// <summary>A suspended reseller's deletion is refused as a conflict and the plan survives.</summary>
    [Fact]
    public async Task A_suspended_resellers_deletion_is_refused_and_the_plan_survives()
    {
        var plan = _scenario.AddPlan("Gold", _scenario.ResellerId);

        var result = await _scenario.DeleteAsReseller().HandleAsync(
            new DeletePlanCommand(plan.Id, "203.0.113.7", "unit-tests"), CancellationToken.None);

        Assert.Equal("ResellerSuspended", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.NotNull(await _scenario.ReadPlanAsync(plan.Id));
        var entry = Assert.Single(_scenario.Audit.Entries);
        Assert.Equal(AccountAuditActions.PlanDeleted, entry.Action);
        Assert.False(entry.Succeeded);
    }
}
