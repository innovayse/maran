using Maran.Modules.Tasks.Domain.Entities;
using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Queries.ListTasks;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Tasks.Tests.Queries.ListTasks;

/// <summary>Behavioral contract of <see cref="ListTasksQueryValidator"/>.</summary>
public sealed class ListTasksQueryValidatorTests
{
    /// <summary>An instant the window tests are measured from.</summary>
    private static readonly DateTimeOffset Instant = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The validator under test.</summary>
    private readonly ListTasksQueryValidator _validator = new();

    /// <summary>The defaults and every known filter are accepted — the inverse control for the refusals below.</summary>
    [Fact]
    public void The_defaults_and_every_known_filter_are_accepted()
    {
        Assert.True(_validator.Validate(new ListTasksQuery()).IsValid);
        Assert.True(_validator.Validate(new ListTasksQuery(
            Page: 3,
            PageSize: 10,
            Search: SearchTerm.Of("shop"),
            Sort: SortRequest.Parse("-subject"),
            AccountId: Guid.NewGuid(),
            Status: PanelTaskStatus.Failed,
            Kind: FilterValue.Of("BackupCreate"),
            From: Instant,
            To: Instant.AddDays(1))).IsValid);
    }

    /// <summary>Every sort the list exposes is accepted in both directions.</summary>
    /// <param name="sort">The sort.</param>
    [Theory]
    [InlineData("startedAt")]
    [InlineData("-startedAt")]
    [InlineData("subject")]
    [InlineData("-subject")]
    public void Every_sort_the_list_exposes_is_accepted_in_both_directions(string sort)
    {
        Assert.True(_validator.Validate(new ListTasksQuery(Sort: SortRequest.Parse(sort))).IsValid);
    }

    /// <summary>A sort the list does not expose is refused with the paging contract's code.</summary>
    /// <param name="sort">The sort.</param>
    [Theory]
    [InlineData("createdAt")]
    [InlineData("log")]
    [InlineData("StartedAt")]
    public void A_sort_the_list_does_not_expose_is_refused(string sort)
    {
        var result = _validator.Validate(new ListTasksQuery(Sort: SortRequest.Parse(sort)));

        Assert.Equal("ListSortUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page below the first and a size the screens do not offer are refused.</summary>
    [Fact]
    public void A_page_below_the_first_and_an_unoffered_size_are_refused()
    {
        Assert.Equal(
            "ListPageOutOfRange",
            Assert.Single(_validator.Validate(new ListTasksQuery(Page: 0)).Errors).ErrorCode);
        Assert.Equal(
            "ListPageSizeNotOffered",
            Assert.Single(_validator.Validate(new ListTasksQuery(PageSize: 200)).Errors).ErrorCode);
    }

    /// <summary>A status that names no member of its enum is refused with its own code.</summary>
    [Fact]
    public void A_status_that_names_no_member_is_refused()
    {
        var result = _validator.Validate(new ListTasksQuery(Status: (PanelTaskStatus)99));

        Assert.Equal("TaskStatusUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A kind filter the column could never hold is refused with its own code.</summary>
    /// <param name="kind">The kind filter.</param>
    [Theory]
    [InlineData("Backup Create")]
    [InlineData("Backup%")]
    [InlineData("a\rb")]
    public void A_kind_filter_the_column_could_never_hold_is_refused(string kind)
    {
        var result = _validator.Validate(new ListTasksQuery(Kind: FilterValue.Of(kind)));

        Assert.Equal("TaskKindFilterInvalid", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A kind longer than the column is refused; the longest legal one and no kind at all are not.</summary>
    [Fact]
    public void A_kind_longer_than_the_column_is_refused_and_none_is_not()
    {
        Assert.Equal(
            "TaskKindFilterInvalid",
            Assert.Single(_validator.Validate(new ListTasksQuery(
                Kind: FilterValue.Of(new string('K', PanelTask.KindMaxLength + 1)))).Errors).ErrorCode);
        Assert.True(_validator.Validate(new ListTasksQuery(Kind: FilterValue.None)).IsValid);
        Assert.True(_validator.Validate(new ListTasksQuery(Kind: FilterValue.Of(new string('K', PanelTask.KindMaxLength)))).IsValid);
    }

    /// <summary>A window whose start is later than its end is refused.</summary>
    [Fact]
    public void A_window_whose_start_is_later_than_its_end_is_refused()
    {
        var result = _validator.Validate(new ListTasksQuery(From: Instant.AddSeconds(1), To: Instant));

        Assert.Equal("TaskDateRangeInverted", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A window of one instant, and a window open at either end, are accepted.</summary>
    [Fact]
    public void A_one_instant_window_and_a_half_open_window_are_accepted()
    {
        Assert.True(_validator.Validate(new ListTasksQuery(From: Instant, To: Instant)).IsValid);
        Assert.True(_validator.Validate(new ListTasksQuery(From: Instant)).IsValid);
        Assert.True(_validator.Validate(new ListTasksQuery(To: Instant)).IsValid);
    }
}
