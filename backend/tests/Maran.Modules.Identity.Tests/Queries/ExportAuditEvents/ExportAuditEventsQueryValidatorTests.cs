using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Queries.ExportAuditEvents;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Identity.Tests.Queries.ExportAuditEvents;

/// <summary>Behavioral contract of <see cref="ExportAuditEventsQueryValidator"/>.</summary>
public sealed class ExportAuditEventsQueryValidatorTests
{
    /// <summary>An instant the window tests are measured from.</summary>
    private static readonly DateTimeOffset Instant = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The validator under test.</summary>
    private readonly ExportAuditEventsQueryValidator _validator = new();

    /// <summary>The defaults and every known filter are accepted — the inverse control for the refusals below.</summary>
    [Fact]
    public void The_defaults_and_every_known_filter_are_accepted()
    {
        Assert.True(_validator.Validate(new ExportAuditEventsQuery()).IsValid);
        Assert.True(_validator.Validate(new ExportAuditEventsQuery(
            Format: AuditExportFormat.Csv,
            Action: FilterValue.Of(IdentityAuditActions.LoginFailed),
            Outcome: AuditOutcome.Failed,
            From: Instant,
            To: Instant.AddDays(1),
            Search: SearchTerm.Of("alice"),
            Actor: FilterValue.Of("olive.ops-1_a"),
            Account: Guid.NewGuid())).IsValid);
    }

    /// <summary>Both formats the enum names are accepted.</summary>
    /// <param name="format">The format.</param>
    [Theory]
    [InlineData(AuditExportFormat.Csv)]
    [InlineData(AuditExportFormat.Json)]
    public void Both_known_formats_are_accepted(AuditExportFormat format)
    {
        Assert.True(_validator.Validate(new ExportAuditEventsQuery(Format: format)).IsValid);
    }

    /// <summary>A format naming no member is refused with its own code.</summary>
    [Fact]
    public void A_format_naming_no_member_is_refused()
    {
        var result = _validator.Validate(new ExportAuditEventsQuery(Format: (AuditExportFormat)7));

        Assert.Equal("AuditExportFormatUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>An action filter the column could never hold is refused with the journal list's code.</summary>
    /// <param name="action">The action filter.</param>
    [Theory]
    [InlineData("Login Failed")]
    [InlineData("Login%")]
    [InlineData("a\nb")]
    public void An_action_filter_the_column_could_never_hold_is_refused(string action)
    {
        var result = _validator.Validate(new ExportAuditEventsQuery(Action: FilterValue.Of(action)));

        Assert.Equal("AuditActionFilterInvalid", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>An action longer than the column is refused; the longest legal one is accepted.</summary>
    [Fact]
    public void An_action_longer_than_the_column_is_refused_and_the_longest_legal_one_is_not()
    {
        var longest = new string('a', AuditEvent.ActionMaxLength);

        Assert.True(_validator.Validate(new ExportAuditEventsQuery(Action: FilterValue.Of(longest))).IsValid);
        Assert.Equal(
            "AuditActionFilterInvalid",
            Assert.Single(_validator.Validate(new ExportAuditEventsQuery(Action: FilterValue.Of(longest + "a"))).Errors).ErrorCode);
    }

    /// <summary>An actor filter outside the login alphabet or over 64 characters is refused with its own code.</summary>
    /// <param name="actor">The actor filter.</param>
    [Theory]
    [InlineData("olive ops")]
    [InlineData("olive%")]
    [InlineData("olive@example.com")]
    [InlineData("a\tb")]
    public void An_actor_filter_outside_the_alphabet_is_refused(string actor)
    {
        var result = _validator.Validate(new ExportAuditEventsQuery(Actor: FilterValue.Of(actor)));

        Assert.Equal("AuditActorFilterInvalid", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>An actor over the column's width is refused; one exactly as wide is accepted.</summary>
    [Fact]
    public void An_actor_over_the_columns_width_is_refused_and_one_exactly_as_wide_is_not()
    {
        var widest = new string('a', AuditEvent.ActorUsernameMaxLength);

        Assert.True(_validator.Validate(new ExportAuditEventsQuery(Actor: FilterValue.Of(widest))).IsValid);
        Assert.Equal(
            "AuditActorFilterInvalid",
            Assert.Single(_validator.Validate(new ExportAuditEventsQuery(Actor: FilterValue.Of(widest + "a"))).Errors).ErrorCode);
    }

    /// <summary>An outcome naming no member is refused.</summary>
    [Fact]
    public void An_outcome_naming_no_member_is_refused()
    {
        var result = _validator.Validate(new ExportAuditEventsQuery(Outcome: (AuditOutcome)7));

        Assert.Equal("AuditOutcomeUnknown", Assert.Single(result.Errors).ErrorCode);
    }

    /// <summary>A window whose start is after its end is refused; equal bounds and a single bound are accepted.</summary>
    [Fact]
    public void An_inverted_window_is_refused_and_equal_or_single_bounds_are_not()
    {
        Assert.Equal(
            "AuditDateRangeInverted",
            Assert.Single(_validator.Validate(new ExportAuditEventsQuery(From: Instant.AddDays(1), To: Instant)).Errors).ErrorCode);
        Assert.True(_validator.Validate(new ExportAuditEventsQuery(From: Instant, To: Instant)).IsValid);
        Assert.True(_validator.Validate(new ExportAuditEventsQuery(From: Instant)).IsValid);
        Assert.True(_validator.Validate(new ExportAuditEventsQuery(To: Instant)).IsValid);
    }
}
