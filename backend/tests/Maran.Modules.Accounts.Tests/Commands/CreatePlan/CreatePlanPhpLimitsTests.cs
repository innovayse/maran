using Maran.Modules.Accounts.Commands.CreatePlan;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.CreatePlan;

/// <summary>How a plan creation treats the PHP bounds (issue #138): stated bounds are stored, omitted ones are the default, and the validator holds every figure to the agent's ceiling.</summary>
public sealed class CreatePlanPhpLimitsTests : IDisposable
{
    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Bounds the request states are stored and reported.</summary>
    [Fact]
    public async Task Bounds_the_request_states_are_stored_and_reported()
    {
        var result = await NewHandler().HandleAsync(
            ResellerPlanScenario.Create() with { PhpLimits = new PhpLimitsDto(101, 202, 103, 204) },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new PhpLimitsDto(101, 202, 103, 204), result.Value!.PhpLimits);
        var stored = await _dbContext.Plans.AsNoTracking().SingleAsync();
        Assert.Equal(PhpLimits.Of(101, 202, 103, 204), stored.PhpLimits);
    }

    /// <summary>A request that states no bounds creates the plan with the default.</summary>
    [Fact]
    public async Task A_request_that_states_no_bounds_creates_the_plan_with_the_default()
    {
        var result = await NewHandler().HandleAsync(ResellerPlanScenario.Create(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var snapshot = PhpLimitsSnapshot.Default;
        Assert.Equal(
            new PhpLimitsDto(snapshot.MemoryLimitMaxMb, snapshot.UploadMaxMb, snapshot.MaxExecutionSeconds, snapshot.MaxInputVars),
            result.Value!.PhpLimits);
        Assert.Equal(PhpLimits.Default, (await _dbContext.Plans.AsNoTracking().SingleAsync()).PhpLimits);
    }

    /// <summary>A request that states no bounds is valid.</summary>
    [Fact]
    public async Task A_request_that_states_no_bounds_is_valid()
    {
        var verdict = await new CreatePlanCommandValidator().ValidateAsync(ResellerPlanScenario.Create());

        Assert.True(verdict.IsValid);
    }

    /// <summary>Bounds exactly on the agent's ceilings and exactly one are valid.</summary>
    [Fact]
    public async Task Bounds_exactly_on_the_ceilings_and_exactly_one_are_valid()
    {
        var validator = new CreatePlanCommandValidator();
        var top = new PhpLimitsDto(
            PhpSettingCeilings.MaxSizeMb,
            PhpSettingCeilings.MaxSizeMb,
            PhpSettingCeilings.MaxExecutionSeconds,
            PhpSettingCeilings.MaxInputVars);

        Assert.True((await validator.ValidateAsync(ResellerPlanScenario.Create() with { PhpLimits = top })).IsValid);
        Assert.True((await validator.ValidateAsync(
            ResellerPlanScenario.Create() with { PhpLimits = new PhpLimitsDto(1, 1, 1, 1) })).IsValid);
    }

    /// <summary>A stated bound below one or above the agent's ceiling is refused on that bound alone.</summary>
    [Theory]
    [InlineData(0, 64, 120, 3000, nameof(PhpLimitsDto.MemoryLimitMaxMb))]
    [InlineData(PhpSettingCeilings.MaxSizeMb + 1, 64, 120, 3000, nameof(PhpLimitsDto.MemoryLimitMaxMb))]
    [InlineData(256, 0, 120, 3000, nameof(PhpLimitsDto.UploadMaxMb))]
    [InlineData(256, PhpSettingCeilings.MaxSizeMb + 1, 120, 3000, nameof(PhpLimitsDto.UploadMaxMb))]
    [InlineData(256, 64, 0, 3000, nameof(PhpLimitsDto.MaxExecutionSeconds))]
    [InlineData(256, 64, PhpSettingCeilings.MaxExecutionSeconds + 1, 3000, nameof(PhpLimitsDto.MaxExecutionSeconds))]
    [InlineData(256, 64, 120, 0, nameof(PhpLimitsDto.MaxInputVars))]
    [InlineData(256, 64, 120, PhpSettingCeilings.MaxInputVars + 1, nameof(PhpLimitsDto.MaxInputVars))]
    public async Task A_stated_bound_out_of_range_is_refused_on_that_bound_alone(
        int memory,
        int upload,
        int seconds,
        int variables,
        string bound)
    {
        var verdict = await new CreatePlanCommandValidator().ValidateAsync(
            ResellerPlanScenario.Create() with { PhpLimits = new PhpLimitsDto(memory, upload, seconds, variables) });

        Assert.False(verdict.IsValid);
        var error = Assert.Single(verdict.Errors);
        Assert.Equal($"{nameof(CreatePlanCommand.PhpLimits)}.{bound}", error.PropertyName);
    }

    /// <summary>Builds the handler over this test's context.</summary>
    /// <returns>The handler.</returns>
    private CreatePlanCommandHandler NewHandler()
    {
        return PlanHandlerFactory.Create(_dbContext, FakeCurrentUser.Admin(), new RecordingAuditWriter(), new StubDisplayNames());
    }
}
