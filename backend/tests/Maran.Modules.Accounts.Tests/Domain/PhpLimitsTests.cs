using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Domain;

/// <summary>The rules <see cref="PhpLimits"/> holds itself to, and how a plan carries it (issue #138).</summary>
public sealed class PhpLimitsTests
{
    /// <summary>A bound of zero or below is refused: it is a customer who may set nothing, not a smaller allowance.</summary>
    [Theory]
    [InlineData(0, 64, 120, 3000, "memoryLimitMaxMb")]
    [InlineData(256, 0, 120, 3000, "uploadMaxMb")]
    [InlineData(256, 64, 0, 3000, "maxExecutionSeconds")]
    [InlineData(256, 64, 120, 0, "maxInputVars")]
    [InlineData(-1, 64, 120, 3000, "memoryLimitMaxMb")]
    public void A_bound_below_one_is_refused_naming_the_bound(
        int memory,
        int upload,
        int seconds,
        int variables,
        string parameter)
    {
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            PhpLimits.Of(memory, upload, seconds, variables);
        });

        Assert.Equal(parameter, refusal.ParamName);
    }

    /// <summary>A bound above the agent's ceiling is refused: the host would never write it.</summary>
    [Theory]
    [InlineData(PhpSettingCeilings.MaxSizeMb + 1, 64, 120, 3000, "memoryLimitMaxMb")]
    [InlineData(256, PhpSettingCeilings.MaxSizeMb + 1, 120, 3000, "uploadMaxMb")]
    [InlineData(256, 64, PhpSettingCeilings.MaxExecutionSeconds + 1, 3000, "maxExecutionSeconds")]
    [InlineData(256, 64, 120, PhpSettingCeilings.MaxInputVars + 1, "maxInputVars")]
    public void A_bound_above_the_agents_ceiling_is_refused_naming_the_bound(
        int memory,
        int upload,
        int seconds,
        int variables,
        string parameter)
    {
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            PhpLimits.Of(memory, upload, seconds, variables);
        });

        Assert.Equal(parameter, refusal.ParamName);
    }

    /// <summary>A bound exactly on the agent's ceiling, and exactly one, is accepted.</summary>
    [Fact]
    public void A_bound_exactly_on_the_ceiling_and_exactly_one_is_accepted()
    {
        var top = PhpLimits.Of(
            PhpSettingCeilings.MaxSizeMb,
            PhpSettingCeilings.MaxSizeMb,
            PhpSettingCeilings.MaxExecutionSeconds,
            PhpSettingCeilings.MaxInputVars);
        var bottom = PhpLimits.Of(1, 1, 1, 1);

        Assert.Equal(PhpSettingCeilings.MaxInputVars, top.MaxInputVars);
        Assert.Equal(1, bottom.MemoryLimitMaxMb);
    }

    /// <summary>The default is the Sdk snapshot's own figures, so there is one source of them.</summary>
    [Fact]
    public void The_default_is_the_sdk_snapshots_figures()
    {
        var limits = PhpLimits.Default;
        var snapshot = PhpLimitsSnapshot.Default;

        Assert.Equal(snapshot.MemoryLimitMaxMb, limits.MemoryLimitMaxMb);
        Assert.Equal(snapshot.UploadMaxMb, limits.UploadMaxMb);
        Assert.Equal(snapshot.MaxExecutionSeconds, limits.MaxExecutionSeconds);
        Assert.Equal(snapshot.MaxInputVars, limits.MaxInputVars);
    }

    /// <summary>Each read of the default is its own instance, so two plans never share one owned value.</summary>
    [Fact]
    public void Each_read_of_the_default_is_its_own_instance()
    {
        Assert.NotSame(PhpLimits.Default, PhpLimits.Default);
        Assert.Equal(PhpLimits.Default, PhpLimits.Default);
    }

    /// <summary>A plan that never stated bounds carries the default.</summary>
    [Fact]
    public void A_plan_that_never_stated_bounds_carries_the_default()
    {
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);

        Assert.Equal(PhpLimits.Default, plan.PhpLimits);
    }

    /// <summary>Changing a plan's bounds replaces them whole.</summary>
    [Fact]
    public void Changing_a_plans_bounds_replaces_them_whole()
    {
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);

        plan.ChangePhpLimits(PhpLimits.Of(512, 128, 300, 10_000));

        Assert.Equal(PhpLimits.Of(512, 128, 300, 10_000), plan.PhpLimits);
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

    /// <summary>The four bounds are stored in four explicitly named, required columns of the plan's table.</summary>
    [Fact]
    public void The_four_bounds_are_stored_in_four_named_required_columns_of_the_plans_table()
    {
        using var context = AccountsTestContext.Create();
        var owned = context.Model.FindEntityType(typeof(Plan))!.FindNavigation(nameof(Plan.PhpLimits))!;

        Assert.True(owned.ForeignKey.IsRequiredDependent);
        Assert.Equal("PhpMemoryLimitMaxMb", owned.TargetEntityType.FindProperty(nameof(PhpLimits.MemoryLimitMaxMb))!.GetColumnName());
        Assert.Equal("PhpUploadMaxMb", owned.TargetEntityType.FindProperty(nameof(PhpLimits.UploadMaxMb))!.GetColumnName());
        Assert.Equal("PhpMaxExecutionSeconds", owned.TargetEntityType.FindProperty(nameof(PhpLimits.MaxExecutionSeconds))!.GetColumnName());
        Assert.Equal("PhpMaxInputVars", owned.TargetEntityType.FindProperty(nameof(PhpLimits.MaxInputVars))!.GetColumnName());
    }

    /// <summary>Bounds a plan was saved with come back from the store unchanged.</summary>
    [Fact]
    public async Task Bounds_a_plan_was_saved_with_come_back_from_the_store_unchanged()
    {
        var database = Guid.NewGuid().ToString();
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);
        plan.ChangePhpLimits(PhpLimits.Of(128, 32, 60, 1_000));
        await using (var writer = AccountsTestContext.CreateOn(database, FakeCurrentUser.Admin()))
        {
            writer.Plans.Add(plan);
            await writer.SaveChangesAsync();
        }

        await using var reader = AccountsTestContext.CreateOn(database, FakeCurrentUser.Admin());
        var stored = await reader.Plans.AsNoTracking().SingleAsync();

        Assert.Equal(PhpLimits.Of(128, 32, 60, 1_000), stored.PhpLimits);
    }
}
