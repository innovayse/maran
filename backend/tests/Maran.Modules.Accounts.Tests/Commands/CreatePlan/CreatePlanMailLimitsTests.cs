using Maran.Modules.Accounts.Commands.CreatePlan;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.CreatePlan;

/// <summary>How a plan creation treats the mail figures (issue #177): stated figures are stored, omitted ones are the default, and the validator holds every figure to its range.</summary>
public sealed class CreatePlanMailLimitsTests : IDisposable
{
    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Figures the request states are stored and reported.</summary>
    [Fact]
    public async Task Figures_the_request_states_are_stored_and_reported()
    {
        var result = await NewHandler().HandleAsync(
            ResellerPlanScenario.Create() with { MailLimits = new MailLimitsDto(11, 202, 33, 4_404) },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new MailLimitsDto(11, 202, 33, 4_404), result.Value!.MailLimits);
        var stored = await _dbContext.Plans.AsNoTracking().SingleAsync();
        Assert.Equal(MailLimits.Of(11, 202, 33, 4_404), stored.MailLimits);
    }

    /// <summary>A request that states no figures creates the plan with the default.</summary>
    [Fact]
    public async Task A_request_that_states_no_figures_creates_the_plan_with_the_default()
    {
        var result = await NewHandler().HandleAsync(ResellerPlanScenario.Create(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var snapshot = MailLimitsSnapshot.Default;
        Assert.Equal(
            new MailLimitsDto(
                snapshot.MaxMailboxes,
                snapshot.DefaultMailboxQuotaMib,
                snapshot.MailboxSendLimitPerHour,
                snapshot.MailboxQuotaPoolMib),
            result.Value!.MailLimits);
        Assert.Equal(MailLimits.Default, (await _dbContext.Plans.AsNoTracking().SingleAsync()).MailLimits);
    }

    /// <summary>A request that states no figures is valid.</summary>
    [Fact]
    public async Task A_request_that_states_no_figures_is_valid()
    {
        var verdict = await new CreatePlanCommandValidator().ValidateAsync(ResellerPlanScenario.Create());

        Assert.True(verdict.IsValid);
    }

    /// <summary>Figures exactly on their bounds are valid.</summary>
    [Fact]
    public async Task Figures_exactly_on_their_bounds_are_valid()
    {
        var validator = new CreatePlanCommandValidator();
        var top = new MailLimitsDto(
            MailLimits.MaxMailboxesBound,
            MailLimits.MaxQuotaMib,
            MailLimits.MaxSendLimitPerHourBound,
            MailLimits.MaxQuotaMib);

        Assert.True((await validator.ValidateAsync(ResellerPlanScenario.Create() with { MailLimits = top })).IsValid);
        Assert.True((await validator.ValidateAsync(
            ResellerPlanScenario.Create() with { MailLimits = new MailLimitsDto(0, 1, 1, 1) })).IsValid);
    }

    /// <summary>A stated figure out of range is refused on that figure alone.</summary>
    [Theory]
    [InlineData(-1, 1_024, 100, 10_240, nameof(MailLimitsDto.MaxMailboxes))]
    [InlineData(MailLimits.MaxMailboxesBound + 1, 1_024, 100, 10_240, nameof(MailLimitsDto.MaxMailboxes))]
    [InlineData(10, 0, 100, 10_240, nameof(MailLimitsDto.DefaultMailboxQuotaMib))]
    [InlineData(10, 1_024, 0, 10_240, nameof(MailLimitsDto.MailboxSendLimitPerHour))]
    [InlineData(10, 1_024, MailLimits.MaxSendLimitPerHourBound + 1, 10_240, nameof(MailLimitsDto.MailboxSendLimitPerHour))]
    [InlineData(10, 1, 100, 0, nameof(MailLimitsDto.MailboxQuotaPoolMib))]
    [InlineData(10, 1, 100, MailLimits.MaxQuotaMib + 1, nameof(MailLimitsDto.MailboxQuotaPoolMib))]
    public async Task A_stated_figure_out_of_range_is_refused_on_that_figure_alone(
        int mailboxes,
        int quota,
        int sendLimit,
        int pool,
        string figure)
    {
        var verdict = await new CreatePlanCommandValidator().ValidateAsync(
            ResellerPlanScenario.Create() with { MailLimits = new MailLimitsDto(mailboxes, quota, sendLimit, pool) });

        Assert.False(verdict.IsValid);
        var error = Assert.Single(verdict.Errors);
        Assert.Equal($"{nameof(CreatePlanCommand.MailLimits)}.{figure}", error.PropertyName);
    }

    /// <summary>A default quota larger than the pool is refused with the plan mail code, so the value's guard never throws.</summary>
    [Fact]
    public async Task A_default_quota_larger_than_the_pool_is_refused_with_the_plan_mail_code()
    {
        var verdict = await new CreatePlanCommandValidator().ValidateAsync(
            ResellerPlanScenario.Create() with { MailLimits = new MailLimitsDto(10, 2_048, 100, 1_024) });

        var error = Assert.Single(verdict.Errors);
        Assert.Equal($"{nameof(CreatePlanCommand.MailLimits)}.{nameof(MailLimitsDto.DefaultMailboxQuotaMib)}", error.PropertyName);
        Assert.Equal("PlanMailLimitsInvalid", error.ErrorMessage);
    }

    /// <summary>Builds the handler over this test's context.</summary>
    /// <returns>The handler.</returns>
    private CreatePlanCommandHandler NewHandler()
    {
        return PlanHandlerFactory.Create(_dbContext, FakeCurrentUser.Admin(), new RecordingAuditWriter(), new StubDisplayNames());
    }
}
