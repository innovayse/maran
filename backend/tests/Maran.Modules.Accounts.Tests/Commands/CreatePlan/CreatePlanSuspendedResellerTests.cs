using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.CreatePlan;

/// <summary>
/// Behavioral contract of plan creation for a suspended reseller: its live access token can still look
/// but can no longer write, and the state is read from the row, not from the token.
/// </summary>
public sealed class CreatePlanSuspendedResellerTests : IDisposable
{
    /// <summary>A reseller stored already suspended.</summary>
    private readonly ResellerPlanScenario _suspended = new(suspended: true);

    /// <summary>A reseller that is active, the inverse control.</summary>
    private readonly ResellerPlanScenario _active = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _suspended.Dispose();
        _active.Dispose();
    }

    /// <summary>A suspended reseller is refused as a conflict, nothing is stored and the refusal is journalled.</summary>
    [Fact]
    public async Task A_suspended_reseller_is_refused_and_nothing_is_stored()
    {
        var result = await _suspended.CreateAsReseller().HandleAsync(
            ResellerPlanScenario.Create(), CancellationToken.None);

        Assert.Equal("ResellerSuspended", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(await _suspended.Seed.Plans.ToListAsync());
        var entry = Assert.Single(_suspended.Audit.Entries);
        Assert.Equal(AccountAuditActions.PlanCreated, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>An active reseller is not refused, so the check above is not a refusal of everything.</summary>
    [Fact]
    public async Task An_active_reseller_is_not_refused()
    {
        var result = await _active.CreateAsReseller().HandleAsync(
            ResellerPlanScenario.Create(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>Suspension does not stop an administrator from defining a plan.</summary>
    [Fact]
    public async Task Suspension_does_not_stop_an_administrator_from_defining_a_plan()
    {
        var result = await _suspended.CreateAsAdministrator().HandleAsync(
            ResellerPlanScenario.Create(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
