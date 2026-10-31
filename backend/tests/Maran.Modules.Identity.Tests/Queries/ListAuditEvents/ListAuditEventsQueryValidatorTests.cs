using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Queries.ListAuditEvents;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Identity.Tests.Queries.ListAuditEvents;

/// <summary>Behavioral contract of <see cref="ListAuditEventsQueryValidator"/>.</summary>
public sealed class ListAuditEventsQueryValidatorTests
{
    /// <summary>An instant the window tests are measured from.</summary>
    private static readonly DateTimeOffset Instant = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The validator under test.</summary>
    private readonly ListAuditEventsQueryValidator _validator = new();

    /// <summary>The defaults and every known filter are accepted — the inverse control for the refusals below.</summary>
    [Fact]
    public void The_defaults_and_every_known_filter_are_accepted()
    {
        Assert.True(_validator.Validate(new ListAuditEventsQuery()).IsValid);
        Assert.True(_validator.Validate(new ListAuditEventsQuery(
            Page: 2,
            PageSize: 100,
            Search: SearchTerm.Of("alice"),
            Sort: SortRequest.Parse("-actor"),
            Action: FilterValue.Of(AuditActions.LoginFailed),
            Outcome: AuditOutcome.Failed,
            From: Instant,
            To: Instant.AddDays(1))).IsValid);
    }

    /// <summary>Every sort the journal exposes is accepted in both directions.</summary>
    /// <param name="sort">The sort.</param>
    [Theory]
    [InlineData("occurredAt")]
    [InlineData("-occurredAt")]
    [InlineData("actor")]
    [InlineData("-actor")]
    [InlineData("action")]
    [InlineData("-action")]
    public void Every_sort_the_journal_exposes_is_accepted_in_both_directions(string sort)
    {
        Assert.True(_validator.Validate(new ListAuditEventsQuery(Sort: SortRequest.Parse(sort))).IsValid);
    }

    /// <summary>A sort the journal does not expose is refused with the paging contract's code.</summary>
    /// <param name="sort">The sort.</param>
    [Theory]
    [InlineData("ipAddress")]
    [InlineData("userAgent")]
    [InlineData("OccurredAt")]
    public void A_sort_the_journal_does_not_expose_is_refused(string sort)
    {
        var result = _validator.Validate(new ListAuditEventsQuery(Sort: SortRequest.Parse(sort)));

        Assert.Equal("ListSortUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A page below the first and a size the screens do not offer are refused.</summary>
    [Fact]
    public void A_page_below_the_first_and_an_unoffered_size_are_refused()
    {
        Assert.Equal(
            "ListPageOutOfRange",
            Assert.Single(_validator.Validate(new ListAuditEventsQuery(Page: 0)).Errors).ErrorCode);
        Assert.Equal(
            "ListPageSizeNotOffered",
            Assert.Single(_validator.Validate(new ListAuditEventsQuery(PageSize: 500)).Errors).ErrorCode);
    }

    /// <summary>An action filter the column could never hold is refused with its own code.</summary>
    /// <param name="action">The action filter.</param>
    [Theory]
    [InlineData("Login Failed")]
    [InlineData("Login%")]
    [InlineData("a\nb")]
    public void An_action_filter_the_column_could_never_hold_is_refused(string action)
    {
        var result = _validator.Validate(new ListAuditEventsQuery(Action: FilterValue.Of(action)));

        Assert.Equal("AuditActionFilterInvalid", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>An action longer than the column is refused; the longest legal one and no action at all are not.</summary>
    [Fact]
    public void An_action_longer_than_the_column_is_refused_and_none_is_not()
    {
        Assert.Equal(
            "AuditActionFilterInvalid",
            Assert.Single(_validator.Validate(new ListAuditEventsQuery(
                Action: FilterValue.Of(new string('A', AuditEvent.ActionMaxLength + 1)))).Errors).ErrorCode);
        Assert.True(_validator.Validate(new ListAuditEventsQuery(Action: FilterValue.None)).IsValid);
        Assert.True(_validator.Validate(new ListAuditEventsQuery(
            Action: FilterValue.Of(new string('A', AuditEvent.ActionMaxLength)))).IsValid);
    }

    /// <summary>An outcome that names no member of its enum is refused with its own code.</summary>
    [Fact]
    public void An_outcome_that_names_no_member_is_refused()
    {
        var result = _validator.Validate(new ListAuditEventsQuery(Outcome: (AuditOutcome)99));

        Assert.Equal("AuditOutcomeUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A window whose start is later than its end is refused.</summary>
    [Fact]
    public void A_window_whose_start_is_later_than_its_end_is_refused()
    {
        var result = _validator.Validate(new ListAuditEventsQuery(From: Instant.AddSeconds(1), To: Instant));

        Assert.Equal("AuditDateRangeInverted", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A window of one instant, and a window open at either end, are accepted.</summary>
    [Fact]
    public void A_one_instant_window_and_a_half_open_window_are_accepted()
    {
        Assert.True(_validator.Validate(new ListAuditEventsQuery(From: Instant, To: Instant)).IsValid);
        Assert.True(_validator.Validate(new ListAuditEventsQuery(From: Instant)).IsValid);
        Assert.True(_validator.Validate(new ListAuditEventsQuery(To: Instant)).IsValid);
    }
}
