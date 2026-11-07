using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Models;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Identity.Tests.Domain.Policies;

/// <summary>
/// Behavioural contract of <see cref="AuditExportSubjectText"/>: the audit subject of an export entry
/// records the format, the planned row count and only the filters that were given.
/// </summary>
public sealed class AuditExportSubjectTextTests
{
    /// <summary>An export with no filter reads as a format and a count and nothing else.</summary>
    [Fact]
    public void An_unfiltered_export_names_only_its_format_and_row_count()
    {
        var subject = AuditExportSubjectText.Of(AuditExportFormat.Json, Filter(), 42);

        Assert.Equal("format=json rows=42", subject);
    }

    /// <summary>Every filter that was given is spelled, in a fixed order, with the search last.</summary>
    [Fact]
    public void Every_given_filter_is_spelled_in_a_fixed_order_with_the_search_last()
    {
        var account = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var filter = new AuditEventFilter(
            FilterValue.Of("LoginFailed"),
            AuditOutcome.Failed,
            from,
            from.AddDays(1),
            SearchTerm.Of("alice smith"),
            FilterValue.Of("olive"),
            account);

        var subject = AuditExportSubjectText.Of(AuditExportFormat.Csv, filter, 7);

        Assert.Equal(
            "format=csv rows=7 action=LoginFailed outcome=failed from=2026-10-01T00:00:00.0000000Z "
            + "to=2026-10-02T00:00:00.0000000Z actor=olive account=11111111-2222-3333-4444-555555555555 "
            + "search=alice smith",
            subject);
    }

    /// <summary>A subject longer than the column is cut to the column's width, not refused.</summary>
    [Fact]
    public void A_subject_longer_than_the_column_is_cut_to_its_width()
    {
        var filter = Filter() with
        {
            Search = SearchTerm.Of(new string('x', PageDefaults.SearchMaxLength)),
            Actor = FilterValue.Of(new string('a', 200)),
        };

        var subject = AuditExportSubjectText.Of(AuditExportFormat.Json, filter, 1);

        Assert.Equal(AuditEvent.SubjectMaxLength, subject.Length);
        Assert.StartsWith("format=json rows=1 actor=", subject, StringComparison.Ordinal);
    }

    /// <summary>The filter with no condition at all.</summary>
    /// <returns>The empty filter.</returns>
    private static AuditEventFilter Filter()
    {
        return new AuditEventFilter(default, null, null, null, default, default, null);
    }
}
