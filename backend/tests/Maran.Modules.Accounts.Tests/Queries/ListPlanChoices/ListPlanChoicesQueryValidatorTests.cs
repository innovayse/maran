using Maran.Modules.Accounts.Queries.ListPlanChoices;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Accounts.Tests.Queries.ListPlanChoices;

/// <summary>Behavioral contract of <see cref="ListPlanChoicesQueryValidator"/>.</summary>
public sealed class ListPlanChoicesQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ListPlanChoicesQueryValidator _validator = new();

    /// <summary>The defaults and the name sort either way are accepted — the inverse control for the refusals below.</summary>
    [Fact]
    public void The_defaults_and_the_name_sort_either_way_are_accepted()
    {
        Assert.True(_validator.Validate(new ListPlanChoicesQuery()).IsValid);
        Assert.True(_validator.Validate(new ListPlanChoicesQuery(Sort: SortRequest.Parse("-name"))).IsValid);
    }

    /// <summary>A sort a picker does not expose is refused, even one the plans list exposes.</summary>
    [Fact]
    public void A_sort_a_picker_does_not_expose_is_refused_even_one_the_plans_list_exposes()
    {
        var result = _validator.Validate(new ListPlanChoicesQuery(Sort: SortRequest.Parse("accounts")));

        Assert.Equal("ListSortUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page size larger than the cap is refused: a picker cannot ask for every plan at once.</summary>
    [Fact]
    public void A_page_size_larger_than_the_cap_is_refused()
    {
        var result = _validator.Validate(new ListPlanChoicesQuery(PageSize: 101));

        Assert.Equal("ListPageSizeNotOffered", Assert.Single(result.Errors).ErrorCode);
    }
}
