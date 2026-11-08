using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Queries.ListMailAliases;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Mail.Tests.Queries.ListMailAliases;

/// <summary>Behavioral contract of <see cref="ListMailAliasesQueryValidator"/>.</summary>
public sealed class ListMailAliasesQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ListMailAliasesQueryValidator _validator = new();

    /// <summary>The defaults and every member set within its bounds are accepted — the inverse control for the refusals below.</summary>
    [Fact]
    public void The_defaults_and_every_member_within_its_bounds_are_accepted()
    {
        Assert.True(_validator.Validate(new ListMailAliasesQuery()).IsValid);
        Assert.True(_validator.Validate(new ListMailAliasesQuery(
            Page: 3,
            PageSize: 50,
            Search: SearchTerm.Of("info"),
            Sort: SortRequest.Parse("-address"),
            AccountId: Guid.NewGuid(),
            Domain: FilterValue.Of("example.com"),
            Kind: MailAliasKind.Forwarder)).IsValid);
    }

    /// <summary>Every sort the list exposes is accepted in both directions.</summary>
    /// <param name="sort">The sort key.</param>
    [Theory]
    [InlineData("address")]
    [InlineData("-address")]
    [InlineData("createdAt")]
    [InlineData("-createdAt")]
    public void Every_sort_the_list_exposes_is_accepted_in_both_directions(string sort)
    {
        Assert.True(_validator.Validate(new ListMailAliasesQuery(Sort: SortRequest.Parse(sort))).IsValid);
    }

    /// <summary>A sort the list does not expose is refused with the paging contract's code.</summary>
    /// <param name="sort">The sort key.</param>
    [Theory]
    [InlineData("localPart")]
    [InlineData("kind")]
    [InlineData("Address")]
    public void A_sort_the_list_does_not_expose_is_refused(string sort)
    {
        var result = _validator.Validate(new ListMailAliasesQuery(Sort: SortRequest.Parse(sort)));

        Assert.Equal("ListSortUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page below the first and a page size that is not offered are refused.</summary>
    [Fact]
    public void A_page_below_the_first_and_an_unoffered_page_size_are_refused()
    {
        var page = _validator.Validate(new ListMailAliasesQuery(Page: 0));
        var size = _validator.Validate(new ListMailAliasesQuery(PageSize: 7));

        Assert.Equal("ListPageOutOfRange", Assert.Single(page.Errors).ErrorCode);
        Assert.Equal("ListPageSizeNotOffered", Assert.Single(size.Errors).ErrorCode);
    }

    /// <summary>A kind that names nothing is refused where no binder stands in front of the validator, and every real kind is accepted.</summary>
    [Fact]
    public void A_kind_that_names_nothing_is_refused_and_every_real_kind_is_accepted()
    {
        Assert.False(_validator.Validate(new ListMailAliasesQuery(Kind: (MailAliasKind)9)).IsValid);

        foreach (var kind in Enum.GetValues<MailAliasKind>())
        {
            Assert.True(_validator.Validate(new ListMailAliasesQuery(Kind: kind)).IsValid);
        }
    }
}
