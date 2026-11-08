using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Queries.ListMailDomains;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Mail.Tests.Queries.ListMailDomains;

/// <summary>The paging bounds and the sort whitelist the mail domain list holds its callers to.</summary>
public sealed class ListMailDomainsQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ListMailDomainsQueryValidator _validator = new();

    /// <summary>The defaults and a fully populated query are accepted.</summary>
    [Fact]
    public void The_defaults_and_every_member_within_its_bounds_are_accepted()
    {
        Assert.True(_validator.Validate(new ListMailDomainsQuery()).IsValid);
        Assert.True(_validator.Validate(new ListMailDomainsQuery(
            Page: 2,
            PageSize: 25,
            Search: SearchTerm.Of("example"),
            Sort: SortRequest.Parse("-name"),
            AccountId: Guid.NewGuid(),
            Status: MailDomainStatus.Suspended)).IsValid);
    }

    /// <summary>Both exposed sorts are accepted in both directions.</summary>
    /// <param name="sort">The sort text.</param>
    [Theory]
    [InlineData("name")]
    [InlineData("-name")]
    [InlineData("createdAt")]
    [InlineData("-createdAt")]
    public void Every_sort_the_list_exposes_is_accepted_in_both_directions(string sort)
    {
        Assert.True(_validator.Validate(new ListMailDomainsQuery(Sort: SortRequest.Parse(sort))).IsValid);
    }

    /// <summary>A sort the list does not expose is refused with the list code.</summary>
    /// <param name="sort">The sort text.</param>
    [Theory]
    [InlineData("status")]
    [InlineData("accountId")]
    [InlineData("Name")]
    public void A_sort_the_list_does_not_expose_is_refused(string sort)
    {
        var result = _validator.Validate(new ListMailDomainsQuery(Sort: SortRequest.Parse(sort)));

        Assert.Equal("ListSortUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page below the first is refused.</summary>
    [Fact]
    public void A_page_below_the_first_is_refused()
    {
        var result = _validator.Validate(new ListMailDomainsQuery(Page: 0));

        Assert.Equal("ListPageOutOfRange", Assert.Single(result.Errors).ErrorCode);
    }
}
