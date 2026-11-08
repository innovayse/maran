using Maran.Modules.Accounts.Commands.UpdatePlan;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.UpdatePlan;

/// <summary>How a plan edit treats the mail figures (issue #177): stated figures replace the plan's and omitted ones leave them.</summary>
public sealed class UpdatePlanMailLimitsTests : IDisposable
{
    /// <summary>The figures the plan under edit starts with: unlike the default and unlike every edit below.</summary>
    private static readonly MailLimitsDto Current = new(7, 256, 40, 3_072);

    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <summary>The plan every test edits.</summary>
    private readonly Plan _plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);

    /// <summary>Stores the plan the tests edit.</summary>
    public UpdatePlanMailLimitsTests()
    {
        _plan.ChangeMailLimits(MailLimitsMapper.ToDomain(Current));
        _dbContext.Plans.Add(_plan);
        _dbContext.SaveChanges();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Stated figures replace the plan's and are reported.</summary>
    [Fact]
    public async Task Stated_figures_replace_the_plans_and_are_reported()
    {
        var stated = new MailLimitsDto(50, 2_048, 200, 51_200);

        var result = await NewHandler().HandleAsync(Edit(stated), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(stated, result.Value!.MailLimits);
        Assert.Equal(MailLimitsMapper.ToDomain(stated), (await ReloadAsync()).MailLimits);
    }

    /// <summary>A request that leaves the figures out keeps the plan's own.</summary>
    [Fact]
    public async Task A_request_that_leaves_the_figures_out_keeps_the_plans_own()
    {
        var result = await NewHandler().HandleAsync(Edit(null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Current, result.Value!.MailLimits);
        Assert.Equal(MailLimitsMapper.ToDomain(Current), (await ReloadAsync()).MailLimits);
    }

    /// <summary>A stated figure out of range is refused by the validator on that figure.</summary>
    [Theory]
    [InlineData(-1, nameof(MailLimitsDto.MaxMailboxes))]
    [InlineData(0, nameof(MailLimitsDto.DefaultMailboxQuotaMib))]
    [InlineData(MailLimits.MaxSendLimitPerHourBound + 1, nameof(MailLimitsDto.MailboxSendLimitPerHour))]
    [InlineData(MailLimits.MaxQuotaMib + 1, nameof(MailLimitsDto.MailboxQuotaPoolMib))]
    public async Task A_stated_figure_out_of_range_is_refused_by_the_validator_on_that_figure(int value, string figure)
    {
        var stated = figure switch
        {
            nameof(MailLimitsDto.MaxMailboxes) => Current with { MaxMailboxes = value },
            nameof(MailLimitsDto.DefaultMailboxQuotaMib) => Current with { DefaultMailboxQuotaMib = value },
            nameof(MailLimitsDto.MailboxSendLimitPerHour) => Current with { MailboxSendLimitPerHour = value },
            _ => Current with { MailboxQuotaPoolMib = value },
        };

        var verdict = await new UpdatePlanCommandValidator().ValidateAsync(Edit(stated));

        var error = Assert.Single(verdict.Errors);
        Assert.Equal($"{nameof(UpdatePlanCommand.MailLimits)}.{figure}", error.PropertyName);
    }

    /// <summary>An edit that leaves the figures out is valid.</summary>
    [Fact]
    public async Task An_edit_that_leaves_the_figures_out_is_valid()
    {
        var verdict = await new UpdatePlanCommandValidator().ValidateAsync(Edit(null));

        Assert.True(verdict.IsValid);
    }

    /// <summary>Builds the handler over this test's context.</summary>
    /// <returns>The handler.</returns>
    private UpdatePlanCommandHandler NewHandler()
    {
        return PlanHandlerFactory.Update(
            _dbContext, FakeCurrentUser.Admin(), new RecordingAuditWriter(), new StubDisplayNames(), new RecordingAgentAccountsClient());
    }

    /// <summary>Builds an edit that changes nothing but the mail figures it states.</summary>
    /// <param name="mailLimits">The figures to state, or <c>null</c> for a request that left them out.</param>
    /// <returns>The command.</returns>
    private UpdatePlanCommand Edit(MailLimitsDto? mailLimits)
    {
        return new UpdatePlanCommand(
            "Agency", 10_240, 10, 5, 4, 2, 3, 8,
            ResourceCeilingTestValues.Unlimited, ResourceCeilingTestValues.Unlimited, ResourceCeilingTestValues.Unlimited,
            [AccountFeature.Mail],
            MailLimits: mailLimits,
            PlanId: _plan.Id,
            IpAddress: "203.0.113.7",
            UserAgent: "unit-tests");
    }

    /// <summary>Re-reads the plan from the store, bypassing what the context already tracks.</summary>
    /// <returns>The stored plan.</returns>
    private async Task<Plan> ReloadAsync()
    {
        return await _dbContext.Plans.AsNoTracking().SingleAsync(plan => plan.Id == _plan.Id);
    }
}
