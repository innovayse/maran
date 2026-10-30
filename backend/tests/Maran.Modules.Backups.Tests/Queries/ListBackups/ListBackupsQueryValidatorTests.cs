using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Queries.ListBackups;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Backups.Tests.Queries.ListBackups;

/// <summary>Behavioral contract of <see cref="ListBackupsQueryValidator"/>.</summary>
public sealed class ListBackupsQueryValidatorTests
{
    /// <summary>An instant the window tests are measured from.</summary>
    private static readonly DateTimeOffset Instant = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The validator under test.</summary>
    private readonly ListBackupsQueryValidator _validator = new();

    /// <summary>The defaults and every known filter are accepted — the inverse control for the refusals below.</summary>
    [Fact]
    public void The_defaults_and_every_known_filter_are_accepted()
    {
        Assert.True(_validator.Validate(new ListBackupsQuery()).IsValid);
        Assert.True(_validator.Validate(new ListBackupsQuery(
            Page: 2,
            PageSize: 50,
            Search: SearchTerm.Of("cust"),
            Sort: SortRequest.Parse("-size"),
            AccountId: Guid.NewGuid(),
            Status: BackupStatus.Failed,
            Kind: BackupKind.PreDeletion,
            From: Instant,
            To: Instant.AddDays(1))).IsValid);
    }

    /// <summary>Every sort the history exposes is accepted in both directions.</summary>
    /// <param name="sort">The sort.</param>
    [Theory]
    [InlineData("startedAt")]
    [InlineData("-startedAt")]
    [InlineData("size")]
    [InlineData("-size")]
    public void Every_sort_the_history_exposes_is_accepted_in_both_directions(string sort)
    {
        Assert.True(_validator.Validate(new ListBackupsQuery(Sort: SortRequest.Parse(sort))).IsValid);
    }

    /// <summary>A sort the history does not expose is refused with the paging contract's code.</summary>
    /// <param name="sort">The sort.</param>
    [Theory]
    [InlineData("sha256")]
    [InlineData("status")]
    [InlineData("StartedAt")]
    public void A_sort_the_history_does_not_expose_is_refused(string sort)
    {
        var result = _validator.Validate(new ListBackupsQuery(Sort: SortRequest.Parse(sort)));

        Assert.Equal("ListSortUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page below the first and a size the screens do not offer are refused.</summary>
    [Fact]
    public void A_page_below_the_first_and_an_unoffered_size_are_refused()
    {
        Assert.Equal(
            "ListPageOutOfRange",
            Assert.Single(_validator.Validate(new ListBackupsQuery(Page: 0)).Errors).ErrorCode);
        Assert.Equal(
            "ListPageSizeNotOffered",
            Assert.Single(_validator.Validate(new ListBackupsQuery(PageSize: 101)).Errors).ErrorCode);
    }

    /// <summary>A status or kind that names no member of its enum is refused with its own code.</summary>
    [Fact]
    public void A_status_or_kind_that_names_no_member_is_refused_with_its_own_code()
    {
        Assert.Equal(
            "BackupStatusUnknown",
            Assert.Single(_validator.Validate(new ListBackupsQuery(Status: (BackupStatus)99)).Errors).ErrorCode);
        Assert.Equal(
            "BackupKindUnknown",
            Assert.Single(_validator.Validate(new ListBackupsQuery(Kind: (BackupKind)99)).Errors).ErrorCode);
    }

    /// <summary>A window whose start is later than its end is refused.</summary>
    [Fact]
    public void A_window_whose_start_is_later_than_its_end_is_refused()
    {
        var result = _validator.Validate(new ListBackupsQuery(From: Instant.AddSeconds(1), To: Instant));

        Assert.Equal("BackupDateRangeInverted", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A window of one instant, and a window open at either end, are accepted.</summary>
    [Fact]
    public void A_one_instant_window_and_a_half_open_window_are_accepted()
    {
        Assert.True(_validator.Validate(new ListBackupsQuery(From: Instant, To: Instant)).IsValid);
        Assert.True(_validator.Validate(new ListBackupsQuery(From: Instant)).IsValid);
        Assert.True(_validator.Validate(new ListBackupsQuery(To: Instant)).IsValid);
    }
}
