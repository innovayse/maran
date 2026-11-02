using Maran.Modules.Accounts.Domain.Entities;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Tests.Domain;

/// <summary>Behavioral contract of the feature set a <see cref="Plan"/> carries (issue #316).</summary>
public sealed class PlanFeaturesTests
{
    /// <summary>A plan that states no features includes every one, so no existing customer loses anything.</summary>
    [Fact]
    public void A_plan_that_states_no_features_includes_every_one()
    {
        var defined = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);
        var constructed = new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, 5, 5);

        Assert.Equal(AccountFeature.All, defined.Features);
        Assert.Equal(AccountFeature.All, constructed.Features);
    }

    /// <summary>A plan defined with a subset includes exactly that subset.</summary>
    [Fact]
    public void A_plan_defined_with_a_subset_includes_exactly_that_subset()
    {
        var plan = Plan.Define(
            Guid.NewGuid(), "NoMail", 10_240, 10, 5, 4, 2, 3, 8, AccountFeature.Databases | AccountFeature.Cron);

        Assert.True(plan.Includes(AccountFeature.Databases));
        Assert.True(plan.Includes(AccountFeature.Cron));
        Assert.False(plan.Includes(AccountFeature.Mail));
        Assert.False(plan.Includes(AccountFeature.Backups));
    }

    /// <summary>Changing the features replaces the set whole, and none is a set a plan may hold.</summary>
    [Fact]
    public void Changing_the_features_replaces_the_set_whole_and_none_is_allowed()
    {
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);

        plan.ChangeFeatures(AccountFeature.Backups);
        Assert.Equal(AccountFeature.Backups, plan.Features);

        plan.ChangeFeatures(AccountFeature.None);
        Assert.Equal(AccountFeature.None, plan.Features);
        Assert.False(plan.Includes(AccountFeature.Backups));
    }

    /// <summary>A set holding a bit that is no feature is refused, at creation and at change.</summary>
    [Fact]
    public void A_set_holding_a_bit_that_is_no_feature_is_refused_at_creation_and_at_change()
    {
        var unknown = (AccountFeature)(1L << 40);
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            return Plan.Define(Guid.NewGuid(), "Bad", 10_240, 10, 5, 4, 2, 3, 8, unknown);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            plan.ChangeFeatures(AccountFeature.Mail | unknown);
        });
        Assert.Equal(AccountFeature.All, plan.Features);
    }

    /// <summary>A plan includes a composite only when it includes every part of it.</summary>
    [Fact]
    public void A_plan_includes_a_composite_only_when_it_includes_every_part_of_it()
    {
        var plan = Plan.Define(
            Guid.NewGuid(), "Half", 10_240, 10, 5, 4, 2, 3, 8, AccountFeature.Mail | AccountFeature.Dns);

        Assert.True(plan.Includes(AccountFeature.Mail | AccountFeature.Dns));
        Assert.False(plan.Includes(AccountFeature.Mail | AccountFeature.Cron));
    }
}
