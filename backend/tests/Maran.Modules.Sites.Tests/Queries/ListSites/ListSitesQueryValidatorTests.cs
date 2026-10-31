using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Queries.ListSites;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Sites.Tests.Queries.ListSites;

/// <summary>Behavioral contract of <see cref="ListSitesQueryValidator"/>.</summary>
public sealed class ListSitesQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ListSitesQueryValidator _validator = new();

    /// <summary>
    /// The defaults and every known filter are accepted — the inverse control for the refusals below.
    /// </summary>
    [Fact]
    public void The_defaults_and_every_known_filter_are_accepted()
    {
        Assert.True(_validator.Validate(new ListSitesQuery()).IsValid);
        Assert.True(_validator.Validate(new ListSitesQuery(
            Page: 3,
            PageSize: 50,
            Search: SearchTerm.Of("shop"),
            Sort: SortRequest.Parse("-domain"),
            AccountId: Guid.NewGuid(),
            BackendType: SiteBackendType.ReverseProxy,
            PhpVersion: FilterValue.Of("8.3"),
            Status: SiteStatus.Disabled)).IsValid);
    }

    /// <summary>Every sort the list exposes is accepted in both directions.</summary>
    [Theory]
    [InlineData("domain")]
    [InlineData("-domain")]
    [InlineData("createdAt")]
    [InlineData("-createdAt")]
    [InlineData("status")]
    [InlineData("-status")]
    public void Every_sort_the_list_exposes_is_accepted_in_both_directions(string sort)
    {
        Assert.True(_validator.Validate(new ListSitesQuery(Sort: SortRequest.Parse(sort))).IsValid);
    }

    /// <summary>A sort the list does not expose is refused with the paging contract's code.</summary>
    [Theory]
    [InlineData("documentRoot")]
    [InlineData("phpVersion")]
    [InlineData("Domain")]
    public void A_sort_the_list_does_not_expose_is_refused(string sort)
    {
        var result = _validator.Validate(new ListSitesQuery(Sort: SortRequest.Parse(sort)));

        Assert.Equal("ListSortUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page size the screens do not offer is refused rather than clamped.</summary>
    [Fact]
    public void A_page_size_the_screens_do_not_offer_is_refused()
    {
        var result = _validator.Validate(new ListSitesQuery(PageSize: 101));

        Assert.Equal("ListPageSizeNotOffered", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A backend type that names no member of the enum is refused with its own code.</summary>
    [Fact]
    public void A_backend_type_that_names_no_member_is_refused_with_its_own_code()
    {
        var result = _validator.Validate(new ListSitesQuery(BackendType: (SiteBackendType)99));

        Assert.Equal("SiteBackendTypeUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A status that names no member of the enum is refused with its own code.</summary>
    [Fact]
    public void A_status_that_names_no_member_is_refused_with_its_own_code()
    {
        var result = _validator.Validate(new ListSitesQuery(Status: (SiteStatus)99));

        Assert.Equal("SiteStatusUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A PHP version filter that is not a version is refused with the module's version code.</summary>
    [Theory]
    [InlineData("8")]
    [InlineData("8.3.1")]
    [InlineData("php8.3")]
    [InlineData("8.3\n")]
    [InlineData("%")]
    public void A_php_version_filter_that_is_not_a_version_is_refused(string phpVersion)
    {
        var result = _validator.Validate(new ListSitesQuery(PhpVersion: FilterValue.Of(phpVersion)));

        Assert.Equal("PhpVersionInvalidFormat", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>No PHP version filter is accepted: it is "every version", the type's own state.</summary>
    [Fact]
    public void No_php_version_filter_is_accepted()
    {
        Assert.True(_validator.Validate(new ListSitesQuery(PhpVersion: FilterValue.None)).IsValid);
    }
}
