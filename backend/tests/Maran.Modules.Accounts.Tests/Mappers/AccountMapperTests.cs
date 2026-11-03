using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Mappers;

/// <summary>
/// Behavioral contract of <see cref="AccountMapper"/> and <see cref="AccountDetailMapper"/>: every
/// field of the account is restated on the wire, and the owner is the id the row carries together with
/// the name the caller looked up, never one decided here.
/// </summary>
public sealed class AccountMapperTests
{
    /// <summary>The instant the account in these tests was created.</summary>
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A reseller's account carries its owner's id and the name it was given.</summary>
    [Fact]
    public void A_resellers_account_carries_its_owners_id_and_the_name_it_was_given()
    {
        var resellerId = Guid.NewGuid();
        var account = new Account(
            Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), Created, resellerId: resellerId);

        var dto = AccountMapper.ToDto(account, new StubDisplayNames(), "Acme Hosting");

        Assert.Equal(account.Id, dto.Id);
        Assert.Equal("acme", dto.Name);
        Assert.Equal(AccountStatus.Active, dto.Status);
        Assert.Equal(resellerId, dto.ResellerId);
        Assert.Equal("Acme Hosting", dto.ResellerName);
    }

    /// <summary>An administrator's account carries no owner id and no owner name.</summary>
    [Fact]
    public void An_administrators_account_carries_no_owner_id_and_no_owner_name()
    {
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), Created);

        var dto = AccountMapper.ToDto(account, new StubDisplayNames(), null);

        Assert.Null(dto.ResellerId);
        Assert.Null(dto.ResellerName);
    }

    /// <summary>The detail view restates the plan's name, the owner's address and the owner of the account.</summary>
    [Fact]
    public void The_detail_view_restates_the_plans_name_the_owners_address_and_the_owner_of_the_account()
    {
        var resellerId = Guid.NewGuid();
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 1_000, 5, 5, 5, 5, 5, 5, resellerId: resellerId);
        var account = new Account(
            Guid.NewGuid(), "acme", "acme.example.com", plan.Id, Created, "owner@example.com", resellerId: resellerId);

        var dto = AccountDetailMapper.ToDto(account, plan, new StubDisplayNames(), "Acme Hosting");

        Assert.Equal("Agency", dto.PlanDisplayName);
        Assert.Equal("owner@example.com", dto.OwnerEmail);
        Assert.Equal(resellerId, dto.ResellerId);
        Assert.Equal("Acme Hosting", dto.ResellerName);
    }

    /// <summary>
    /// An owner id with no name restated stays a null name: the mapper never invents one, so an owner
    /// the caller may not see reads as no name at all.
    /// </summary>
    [Fact]
    public void An_owner_id_with_no_name_restated_stays_a_null_name()
    {
        var account = new Account(
            Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), Created, resellerId: Guid.NewGuid());

        var dto = AccountMapper.ToDto(account, new StubDisplayNames(), null);

        Assert.NotNull(dto.ResellerId);
        Assert.Null(dto.ResellerName);
    }
}
