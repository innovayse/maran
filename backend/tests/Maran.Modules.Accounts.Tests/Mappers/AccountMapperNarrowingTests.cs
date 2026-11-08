using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Mappers;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Mappers;

/// <summary>
/// Behavioral contract of narrowing the single-resource account view to the list view for a caller that
/// must not hold the owner's address: every shared field survives, the address is not in the shape, and
/// a refusal is passed on unchanged.
/// </summary>
public sealed class AccountMapperNarrowingTests
{
    /// <summary>The account view the tests narrow.</summary>
    private static readonly AccountDetailDto Detail = new(
        Guid.NewGuid(),
        "acme",
        "acme.example.com",
        Guid.NewGuid(),
        AccountStatus.Suspended,
        "Suspended",
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        "Basic",
        "owner@example.com",
        Guid.NewGuid(),
        "Acme Hosting");

    /// <summary>Every field the two views share is carried over unchanged.</summary>
    [Fact]
    public void Every_shared_field_is_carried_over_unchanged()
    {
        var dto = AccountMapper.ToDto(Detail);

        Assert.Equal(
            new AccountDto(
                Detail.Id,
                Detail.Name,
                Detail.PrimaryDomain,
                Detail.PlanId,
                Detail.Status,
                Detail.StatusName,
                Detail.CreatedAt,
                Detail.ResellerId,
                Detail.ResellerName),
            dto);
    }

    /// <summary>The narrowed shape has no member that could hold the owner's address or the plan's name.</summary>
    [Fact]
    public void The_narrowed_shape_has_no_member_for_the_owners_address()
    {
        var members = typeof(AccountDto).GetProperties().Select(property =>
        {
            return property.Name;
        });

        Assert.DoesNotContain("OwnerEmail", members);
        Assert.Contains("Name", members);
    }

    /// <summary>A success is narrowed and a failure is passed on whole, error and extension.</summary>
    [Fact]
    public void A_success_is_narrowed_and_a_failure_is_passed_on_unchanged()
    {
        var narrowed = AccountMapper.ToDto(Result<AccountDetailDto>.Ok(Detail));
        var error = Error.Of("AccountNotFound", ErrorType.NotFound);
        var refused = AccountMapper.ToDto(Result<AccountDetailDto>.Fail(error));

        Assert.True(narrowed.IsSuccess);
        Assert.Equal(Detail.Name, narrowed.Value.Name);
        Assert.False(refused.IsSuccess);
        Assert.Equal(error, refused.Error);
    }
}
