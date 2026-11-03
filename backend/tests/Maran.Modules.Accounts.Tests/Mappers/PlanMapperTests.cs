using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Tests.Mappers;

/// <summary>
/// Behavioral contract of <see cref="PlanMapper"/>: a plan is restated field for field, with the owner
/// the plan carries and the name the caller looked up.
/// </summary>
public sealed class PlanMapperTests
{
    /// <summary>A reseller's plan carries its owner's id, the name it was given and the headcount.</summary>
    [Fact]
    public void A_resellers_plan_carries_its_owners_id_the_name_it_was_given_and_the_headcount()
    {
        var resellerId = Guid.NewGuid();
        var plan = Plan.Define(
            Guid.NewGuid(), "Agency", 1_000, 5, 6, 7, 8, 9, 10, AccountFeature.Mail, resellerId);

        var dto = PlanMapper.ToDto(plan, new StubDisplayNames(), 3, "Acme Hosting");

        Assert.Equal("Agency", dto.DisplayName);
        Assert.Equal(1_000, dto.DiskQuotaMb);
        Assert.Equal(6, dto.MaxDatabases);
        Assert.Equal(7, dto.MaxSftpUsers);
        Assert.Equal(8, dto.MaxFtpUsers);
        Assert.Equal(9, dto.MaxCronEntries);
        Assert.Equal(10, dto.MaxPhpWorkersPerPool);
        Assert.Equal([AccountFeature.Mail], dto.Features);
        Assert.Equal(3, dto.AccountCount);
        Assert.Equal(resellerId, dto.ResellerId);
        Assert.Equal("Acme Hosting", dto.ResellerName);
    }

    /// <summary>An administrator's plan carries no owner id and no owner name.</summary>
    [Fact]
    public void An_administrators_plan_carries_no_owner_id_and_no_owner_name()
    {
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 1_000, 5, 5, 5, 5, 5, 5);

        var dto = PlanMapper.ToDto(plan, new StubDisplayNames(), 0, null);

        Assert.Null(dto.ResellerId);
        Assert.Null(dto.ResellerName);
    }

    /// <summary>The picker view restates the identity, the name and the two limits a picker prints.</summary>
    [Fact]
    public void The_picker_view_restates_the_identity_the_name_and_the_two_limits_a_picker_prints()
    {
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 1_000, 5, 5, 5, 5, 5, 5);

        var dto = PlanMapper.ToChoiceDto(plan, new StubDisplayNames());

        Assert.Equal(plan.Id, dto.Id);
        Assert.Equal("Agency", dto.DisplayName);
        Assert.Equal(1_000, dto.DiskQuotaMb);
        Assert.Equal(5, dto.MaxSites);
    }

    /// <summary>A shipped plan's name is resolved and an operator's name is kept verbatim.</summary>
    [Fact]
    public void A_shipped_plans_name_is_resolved_and_an_operators_name_is_kept_verbatim()
    {
        var shipped = new Plan(Guid.NewGuid(), "PlanStarterName", 5_120, 5, 2, 3, 5, 5);
        var named = Plan.Define(Guid.NewGuid(), "Agency", 1_000, 5, 5, 5, 5, 5, 5);
        var names = new StubDisplayNames().With("PlanStarterName", "Starter").With("Agency", "WRONG");

        Assert.Equal("Starter", PlanMapper.DisplayNameOf(shipped, names));
        Assert.Equal("Agency", PlanMapper.DisplayNameOf(named, names));
    }
}
