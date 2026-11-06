using Maran.Modules.Identity.Queries.ListApiKeys;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Identity.Tests.Queries.ListApiKeys;

/// <summary>Behavioural contract of <see cref="ListApiKeysQueryValidator"/>.</summary>
public sealed class ListApiKeysQueryValidatorTests
{
    private readonly ListApiKeysQueryValidator _validator = new();

    /// <summary>The defaults, each status filter and each sort are accepted, the inverse control for the refusals below.</summary>
    [Fact]
    public void The_defaults_each_status_and_each_sort_are_accepted()
    {
        Assert.True(_validator.Validate(new ListApiKeysQuery()).IsValid);

        foreach (var status in ApiKeyStatusFilters.All)
        {
            Assert.True(_validator.Validate(new ListApiKeysQuery(Status: FilterValue.Of(status))).IsValid);
        }

        foreach (var sort in new[] { "name", "-name", "createdAt", "-createdAt", "lastUsedAt", "-lastUsedAt" })
        {
            Assert.True(_validator.Validate(new ListApiKeysQuery(Sort: SortRequest.Parse(sort))).IsValid);
        }
    }

    /// <summary>A status outside the three names is refused with the status code, not answered with an empty page.</summary>
    /// <param name="status">The status sent.</param>
    [Theory]
    [InlineData("deleted")]
    [InlineData("Active")]
    [InlineData("suspended")]
    public void A_status_outside_the_three_is_refused(string status)
    {
        var result = _validator.Validate(new ListApiKeysQuery(Status: FilterValue.Of(status)));

        Assert.Equal("ApiKeyStatusFilterInvalid", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A sort the list does not expose is refused with the paging contract's code.</summary>
    [Fact]
    public void A_sort_the_list_does_not_expose_is_refused()
    {
        var result = _validator.Validate(new ListApiKeysQuery(Sort: SortRequest.Parse("tokenHash")));

        Assert.Equal("ListSortUnknown", Assert.Single(result.Errors).ErrorCode);
    }
}
