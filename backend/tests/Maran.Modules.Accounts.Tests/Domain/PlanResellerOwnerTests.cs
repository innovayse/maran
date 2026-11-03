using Maran.Modules.Accounts.Domain.Entities;

namespace Maran.Modules.Accounts.Tests.Domain;

/// <summary>
/// Behavioral contract of the owner a plan carries: null for the administrator's plans (every plan
/// that existed before resellers, and every plan the panel ships), a reseller's id for a plan that
/// reseller defined, and no way to change it afterwards.
/// </summary>
public sealed class PlanResellerOwnerTests
{
    /// <summary>A plan defined without an owner belongs to the administrator.</summary>
    [Fact]
    public void A_plan_defined_without_an_owner_belongs_to_the_administrator()
    {
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);

        Assert.Null(plan.ResellerId);
    }

    /// <summary>A plan defined for a reseller keeps that reseller's id.</summary>
    [Fact]
    public void A_plan_defined_for_a_reseller_keeps_that_resellers_id()
    {
        var reseller = Guid.NewGuid();

        var plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8, resellerId: reseller);

        Assert.Equal(reseller, plan.ResellerId);
    }

    /// <summary>A plan built through the public constructor can be given an owner too.</summary>
    [Fact]
    public void A_plan_built_through_the_constructor_can_be_given_an_owner()
    {
        var reseller = Guid.NewGuid();

        var owned = new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, 5, 5, resellerId: reseller);
        var shipped = new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, 5, 5);

        Assert.Equal(reseller, owned.ResellerId);
        Assert.Null(shipped.ResellerId);
    }

    /// <summary>No edit of a plan moves it to another owner.</summary>
    [Fact]
    public void No_edit_of_a_plan_moves_it_to_another_owner()
    {
        var reseller = Guid.NewGuid();
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8, resellerId: reseller);

        plan.Rename("Agency Plus");
        plan.ChangeLimits(20_480, 11, 6, 5, 3, 4, 9);

        Assert.Equal(reseller, plan.ResellerId);
    }

    /// <summary>The owner has no public setter, so an edit cannot reach it.</summary>
    [Fact]
    public void The_owner_has_no_public_setter()
    {
        var setter = typeof(Plan).GetProperty(nameof(Plan.ResellerId))!.SetMethod;

        Assert.True(setter is null || !setter.IsPublic);
    }
}
