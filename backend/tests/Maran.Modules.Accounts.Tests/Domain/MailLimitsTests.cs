using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Domain;

/// <summary>The rules <see cref="MailLimits"/> holds itself to, and how a plan carries it (issue #177).</summary>
public sealed class MailLimitsTests
{
    /// <summary>A figure outside its range is refused, naming the figure.</summary>
    [Theory]
    [InlineData(-1, 1_024, 100, 10_240, "maxMailboxes")]
    [InlineData(MailLimits.MaxMailboxesBound + 1, 1_024, 100, 10_240, "maxMailboxes")]
    [InlineData(10, 0, 100, 10_240, "defaultMailboxQuotaMib")]
    [InlineData(10, MailLimits.MaxQuotaMib + 1, 100, MailLimits.MaxQuotaMib, "defaultMailboxQuotaMib")]
    [InlineData(10, 1_024, 0, 10_240, "mailboxSendLimitPerHour")]
    [InlineData(10, 1_024, MailLimits.MaxSendLimitPerHourBound + 1, 10_240, "mailboxSendLimitPerHour")]
    [InlineData(10, 1, 100, 0, "mailboxQuotaPoolMib")]
    [InlineData(10, 1_024, 100, MailLimits.MaxQuotaMib + 1, "mailboxQuotaPoolMib")]
    public void A_figure_outside_its_range_is_refused_naming_the_figure(
        int mailboxes,
        int quota,
        int sendLimit,
        int pool,
        string parameter)
    {
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            MailLimits.Of(mailboxes, quota, sendLimit, pool);
        });

        Assert.Equal(parameter, refusal.ParamName);
    }

    /// <summary>A default quota larger than the pool is refused: the plan would sell a mailbox it cannot hold.</summary>
    [Fact]
    public void A_default_quota_larger_than_the_pool_is_refused()
    {
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            MailLimits.Of(10, 2_048, 100, 2_047);
        });

        Assert.Equal("defaultMailboxQuotaMib", refusal.ParamName);
    }

    /// <summary>A default quota equal to the pool, and a mailbox count of zero, are accepted.</summary>
    [Fact]
    public void A_default_quota_equal_to_the_pool_and_zero_mailboxes_are_accepted()
    {
        var limits = MailLimits.Of(0, 2_048, 1, 2_048);

        Assert.Equal(0, limits.MaxMailboxes);
        Assert.Equal(limits.DefaultMailboxQuotaMib, limits.MailboxQuotaPoolMib);
    }

    /// <summary>Every figure at its upper bound is accepted.</summary>
    [Fact]
    public void Every_figure_at_its_upper_bound_is_accepted()
    {
        var limits = MailLimits.Of(
            MailLimits.MaxMailboxesBound,
            MailLimits.MaxQuotaMib,
            MailLimits.MaxSendLimitPerHourBound,
            MailLimits.MaxQuotaMib);

        Assert.Equal(MailLimits.MaxSendLimitPerHourBound, limits.MailboxSendLimitPerHour);
    }

    /// <summary>The default is the Sdk snapshot's own figures, so there is one source of them.</summary>
    [Fact]
    public void The_default_is_the_sdk_snapshots_figures()
    {
        var limits = MailLimits.Default;
        var snapshot = MailLimitsSnapshot.Default;

        Assert.Equal(snapshot.MaxMailboxes, limits.MaxMailboxes);
        Assert.Equal(snapshot.DefaultMailboxQuotaMib, limits.DefaultMailboxQuotaMib);
        Assert.Equal(snapshot.MailboxSendLimitPerHour, limits.MailboxSendLimitPerHour);
        Assert.Equal(snapshot.MailboxQuotaPoolMib, limits.MailboxQuotaPoolMib);
    }

    /// <summary>Each read of the default is its own instance, so two plans never share one owned value.</summary>
    [Fact]
    public void Each_read_of_the_default_is_its_own_instance()
    {
        Assert.NotSame(MailLimits.Default, MailLimits.Default);
        Assert.Equal(MailLimits.Default, MailLimits.Default);
    }

    /// <summary>A plan that never stated mail figures carries the default.</summary>
    [Fact]
    public void A_plan_that_never_stated_mail_figures_carries_the_default()
    {
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);

        Assert.Equal(MailLimits.Default, plan.MailLimits);
    }

    /// <summary>Changing a plan's mail figures replaces them whole.</summary>
    [Fact]
    public void Changing_a_plans_mail_figures_replaces_them_whole()
    {
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);

        plan.ChangeMailLimits(MailLimits.Of(50, 2_048, 200, 51_200));

        Assert.Equal(MailLimits.Of(50, 2_048, 200, 51_200), plan.MailLimits);
    }

    /// <summary>The plan's cap on mailboxes is its mailbox count, so the resource summary reads it.</summary>
    [Fact]
    public void The_plans_cap_on_mailboxes_is_its_mailbox_count()
    {
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);
        plan.ChangeMailLimits(MailLimits.Of(37, 1_024, 100, 10_240));

        var limit = plan.LimitFor(AccountResourceKind.Mailboxes);

        Assert.Equal(37, limit.Maximum);
    }

    /// <summary>A plan selling no mailbox caps the resource at zero, not at "unlimited".</summary>
    [Fact]
    public void A_plan_selling_no_mailbox_caps_the_resource_at_zero()
    {
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);
        plan.ChangeMailLimits(MailLimits.Of(0, 1_024, 100, 10_240));

        Assert.Equal(0, plan.LimitFor(AccountResourceKind.Mailboxes).Maximum);
    }

    /// <summary>Two plans created with the default save together: each owns its own value.</summary>
    [Fact]
    public async Task Two_plans_created_with_the_default_save_together()
    {
        await using var context = AccountsTestContext.Create();
        context.Plans.Add(Plan.Define(Guid.NewGuid(), "First", 10_240, 10, 5, 4, 2, 3, 8));
        context.Plans.Add(Plan.Define(Guid.NewGuid(), "Second", 10_240, 10, 5, 4, 2, 3, 8));

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.Plans.CountAsync());
    }

    /// <summary>The four figures are stored in four explicitly named, required columns of the plan's table.</summary>
    [Fact]
    public void The_four_figures_are_stored_in_four_named_required_columns_of_the_plans_table()
    {
        using var context = AccountsTestContext.Create();
        var owned = context.Model.FindEntityType(typeof(Plan))!.FindNavigation(nameof(Plan.MailLimits))!;

        Assert.True(owned.ForeignKey.IsRequiredDependent);
        Assert.Equal("MaxMailboxes", owned.TargetEntityType.FindProperty(nameof(MailLimits.MaxMailboxes))!.GetColumnName());
        Assert.Equal("DefaultMailboxQuotaMib", owned.TargetEntityType.FindProperty(nameof(MailLimits.DefaultMailboxQuotaMib))!.GetColumnName());
        Assert.Equal("MailboxSendLimitPerHour", owned.TargetEntityType.FindProperty(nameof(MailLimits.MailboxSendLimitPerHour))!.GetColumnName());
        Assert.Equal("MailboxQuotaPoolMib", owned.TargetEntityType.FindProperty(nameof(MailLimits.MailboxQuotaPoolMib))!.GetColumnName());
    }

    /// <summary>Figures a plan was saved with come back from the store unchanged.</summary>
    [Fact]
    public async Task Figures_a_plan_was_saved_with_come_back_from_the_store_unchanged()
    {
        var database = Guid.NewGuid().ToString();
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);
        plan.ChangeMailLimits(MailLimits.Of(25, 512, 60, 8_192));
        await using (var writer = AccountsTestContext.CreateOn(database, FakeCurrentUser.Admin()))
        {
            writer.Plans.Add(plan);
            await writer.SaveChangesAsync();
        }

        await using var reader = AccountsTestContext.CreateOn(database, FakeCurrentUser.Admin());
        var stored = await reader.Plans.AsNoTracking().SingleAsync();

        Assert.Equal(MailLimits.Of(25, 512, 60, 8_192), stored.MailLimits);
    }
}
