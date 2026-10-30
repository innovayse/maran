using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Queries.ListAccounts;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Accounts.Tests.Queries.ListAccounts;

/// <summary>Behavioral contract of <see cref="ListAccountsQueryValidator"/>.</summary>
public sealed class ListAccountsQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ListAccountsQueryValidator _validator = new();

    /// <summary>The defaults and every known filter are accepted — the inverse control for the refusals below.</summary>
    [Fact]
    public void The_defaults_and_every_known_filter_are_accepted()
    {
        Assert.True(_validator.Validate(new ListAccountsQuery()).IsValid);
        Assert.True(_validator.Validate(new ListAccountsQuery(
            Page: 4,
            PageSize: 100,
            Search: SearchTerm.Of("olive"),
            Sort: SortRequest.Parse("-status"),
            PlanId: Guid.NewGuid(),
            Status: AccountStatus.Suspended)).IsValid);
    }

    /// <summary>A status that names no member of the enum is refused with its own code.</summary>
    [Fact]
    public void A_status_that_names_no_member_is_refused_with_its_own_code()
    {
        var result = _validator.Validate(new ListAccountsQuery(Status: (AccountStatus)7));

        var failure = Assert.Single(result.Errors);
        Assert.Equal("AccountStatusUnknown", failure.ErrorCode);
    }

    /// <summary>A sort the list does not expose is refused with the paging contract's code.</summary>
    [Fact]
    public void A_sort_the_list_does_not_expose_is_refused()
    {
        var result = _validator.Validate(new ListAccountsQuery(Sort: SortRequest.Parse("primaryDomain")));

        Assert.Equal("ListSortUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page size the screens do not offer is refused rather than clamped.</summary>
    [Fact]
    public void A_page_size_the_screens_do_not_offer_is_refused()
    {
        var result = _validator.Validate(new ListAccountsQuery(PageSize: 7));

        Assert.Equal("ListPageSizeNotOffered", Assert.Single(result.Errors).ErrorCode);
    }
}
