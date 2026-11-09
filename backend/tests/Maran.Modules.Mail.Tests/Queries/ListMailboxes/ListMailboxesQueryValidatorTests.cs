using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Queries.ListMailboxes;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Mail.Tests.Queries.ListMailboxes;

/// <summary>Behavioral contract of <see cref="ListMailboxesQueryValidator"/>.</summary>
public sealed class ListMailboxesQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ListMailboxesQueryValidator _validator = new();

    /// <summary>The defaults and every member set within its bounds are accepted — the inverse control for the refusals.</summary>
    [Fact]
    public void The_defaults_and_every_member_within_its_bounds_are_accepted()
    {
        Assert.True(_validator.Validate(new ListMailboxesQuery()).IsValid);
        Assert.True(_validator.Validate(new ListMailboxesQuery(
            Page: 3,
            PageSize: 50,
            Search: SearchTerm.Of("info"),
            Sort: SortRequest.Parse("-quotaMib"),
            AccountId: Guid.NewGuid(),
            Domain: FilterValue.Of("example.com"),
            Status: MailboxStatus.Disabled)).IsValid);
    }

    /// <summary>Every sort the list exposes is accepted in both directions.</summary>
    /// <param name="sort">The sort key.</param>
    [Theory]
    [InlineData("address")]
    [InlineData("-address")]
    [InlineData("createdAt")]
    [InlineData("-createdAt")]
    [InlineData("quotaMib")]
    [InlineData("-quotaMib")]
    public void Every_sort_the_list_exposes_is_accepted_in_both_directions(string sort)
    {
        Assert.True(_validator.Validate(new ListMailboxesQuery(Sort: SortRequest.Parse(sort))).IsValid);
    }

    /// <summary>A sort the list does not expose is refused with the paging contract's code.</summary>
    /// <param name="sort">The sort key.</param>
    [Theory]
    [InlineData("localPart")]
    [InlineData("status")]
    [InlineData("Address")]
    public void A_sort_the_list_does_not_expose_is_refused(string sort)
    {
        var result = _validator.Validate(new ListMailboxesQuery(Sort: SortRequest.Parse(sort)));

        Assert.Equal("ListSortUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page below the first is refused with the paging contract's code.</summary>
    [Fact]
    public void A_page_below_the_first_is_refused()
    {
        var result = _validator.Validate(new ListMailboxesQuery(Page: 0));

        Assert.Equal("ListPageOutOfRange", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A domain filter longer than any domain name is refused with its own code; the longest one is accepted.</summary>
    [Fact]
    public void A_domain_filter_longer_than_a_domain_name_is_refused()
    {
        var accepted = _validator.Validate(new ListMailboxesQuery(
            Domain: FilterValue.Of(new string('a', MailDefaults.DomainMaxLength))));
        var refused = _validator.Validate(new ListMailboxesQuery(
            Domain: FilterValue.Of(new string('a', MailDefaults.DomainMaxLength + 1))));

        Assert.True(accepted.IsValid);
        Assert.Equal(MailboxGroupErrorCodes.MailDomainTooLong, Assert.Single(refused.Errors).ErrorCode);
    }
}
