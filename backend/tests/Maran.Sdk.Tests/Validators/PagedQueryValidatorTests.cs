using Maran.Sdk.Tests.Fixtures;
using Maran.SharedKernel.Pagination;

namespace Maran.Sdk.Tests.Validators;

/// <summary>Behavioural contract of <see cref="Maran.Sdk.Validators.PagedQueryValidator{TQuery, TRow}"/>.</summary>
public sealed class PagedQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ListRequestValidator _validator = new();

    /// <summary>The contract's defaults are a valid request: the inverse control for every refusal below.</summary>
    [Fact]
    public void The_default_request_is_accepted()
    {
        Assert.True(_validator.Validate(new ListRequest()).IsValid);
    }

    /// <summary>
    /// Every allowed size, every page from the first, a mapped sort either way and a search at the
    /// longest length are accepted.
    /// </summary>
    /// <param name="page">A page.</param>
    /// <param name="pageSize">An allowed size.</param>
    /// <param name="sort">A mapped sort.</param>
    [Theory]
    [InlineData(1, 10, "")]
    [InlineData(2, 25, "name")]
    [InlineData(int.MaxValue, 50, "-name")]
    [InlineData(1, 100, "name")]
    public void Requests_inside_every_bound_are_accepted(int page, int pageSize, string sort)
    {
        var request = new ListRequest(
            page,
            pageSize,
            SearchTerm.Of(new string('a', PageDefaults.SearchMaxLength)),
            SortRequest.Parse(sort));

        Assert.True(_validator.Validate(request).IsValid);
    }

    /// <summary>A page below the first is refused with its own code.</summary>
    /// <param name="page">A page below the first.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_page_below_the_first_is_refused(int page)
    {
        AssertRefusedWith(new ListRequest(Page: page), "ListPageOutOfRange");
    }

    /// <summary>
    /// A size outside the offered set is refused with its own code — including one under the maximum.
    /// </summary>
    /// <param name="pageSize">A size that is not offered.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-25)]
    [InlineData(37)]
    [InlineData(PageDefaults.MaximumSize + 1)]
    public void A_page_size_not_offered_is_refused(int pageSize)
    {
        AssertRefusedWith(new ListRequest(PageSize: pageSize), "ListPageSizeNotOffered");
    }

    /// <summary>
    /// A well-formed sort the map does not name is refused with its own code. A malformed one never
    /// reaches the validator: it is refused while binding (<c>SortRequestTests</c>, and the endpoint
    /// tests for the 400 it answers).
    /// </summary>
    /// <param name="sort">An unknown sort.</param>
    [Theory]
    [InlineData("id")]
    [InlineData("Name")]
    [InlineData("-createdAt")]
    public void An_unknown_sort_is_refused(string sort)
    {
        AssertRefusedWith(new ListRequest(Sort: SortRequest.Parse(sort)), "ListSortUnknown");
    }

    /// <summary>
    /// Asserts the request fails with exactly one failure, carrying <paramref name="code"/> as both
    /// its error code and its message — the message is what the panel's exception middleware reads.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    /// <param name="code">The expected code.</param>
    private void AssertRefusedWith(ListRequest request, string code)
    {
        var result = _validator.Validate(request);

        var failure = Assert.Single(result.Errors);
        Assert.Equal(code, failure.ErrorCode);
        Assert.Equal(code, failure.ErrorMessage);
    }
}
