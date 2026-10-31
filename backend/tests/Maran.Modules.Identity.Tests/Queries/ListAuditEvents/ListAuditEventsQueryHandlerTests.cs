using System.Globalization;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Queries.ListAuditEvents;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Identity.Tests.Queries.ListAuditEvents;

/// <summary>
/// What one page of the journal holds: newest first by default, narrowed by action, outcome and
/// window, cut to the page with the true total, each row's action named in the request's language.
/// The search is an <c>ILIKE</c> only PostgreSQL runs, so it is held by <c>AuditEndpointTests</c>.
/// </summary>
public sealed class ListAuditEventsQueryHandlerTests : IDisposable
{
    /// <summary>The instant the seeded entries count from.</summary>
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The in-memory journal the handler reads.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>With no sort named the newest entry comes first, and the oldest sort reverses it.</summary>
    [Fact]
    public async Task With_no_sort_the_newest_entry_comes_first()
    {
        await WriteAsync(Now, "first");
        await WriteAsync(Now.AddMinutes(1), "second");

        Assert.Equal(["second", "first"], await SubjectsAsync(new ListAuditEventsQuery()));
        Assert.Equal(
            ["first", "second"],
            await SubjectsAsync(new ListAuditEventsQuery(Sort: SortRequest.Parse(AuditEventSortFields.OccurredAt))));
    }

    /// <summary>A page holds no more rows than its size, and the total counts every match.</summary>
    [Fact]
    public async Task A_page_holds_its_size_and_the_total_counts_every_match()
    {
        for (var minute = 0; minute < 12; minute++)
        {
            await WriteAsync(Now.AddMinutes(minute), $"entry-{minute:D2}");
        }

        var result = await Handler().HandleAsync(
            new ListAuditEventsQuery(Page: 2, PageSize: 10), CancellationToken.None);

        Assert.Equal(12, result.Value.Total);
        Assert.Equal(["entry-01", "entry-00"], result.Value.Items.Select(auditEvent =>
        {
            return auditEvent.Subject;
        }));
    }

    /// <summary>A page past the end is empty and still carries the true total.</summary>
    [Fact]
    public async Task A_page_past_the_end_is_empty_with_the_true_total()
    {
        await WriteAsync(Now, "only");

        var result = await Handler().HandleAsync(new ListAuditEventsQuery(Page: 5), CancellationToken.None);

        Assert.Equal(1, result.Value.Total);
        Assert.Empty(result.Value.Items);
    }

    /// <summary>The action filter keeps only entries recording that action.</summary>
    [Fact]
    public async Task The_action_filter_keeps_only_that_action()
    {
        await WriteAsync(Now, "signed-in");
        await WriteAsync(Now, "refused", AuditActions.LoginFailed, succeeded: false);

        Assert.Equal(["refused"], await SubjectsAsync(new ListAuditEventsQuery(Action: FilterValue.Of(AuditActions.LoginFailed))));
    }

    /// <summary>The outcome filter keeps one side and the other keeps the other.</summary>
    /// <remarks>Both directions, so a filter that ignored its value and returned everything fails.</remarks>
    [Fact]
    public async Task The_outcome_filter_keeps_only_entries_that_ended_that_way()
    {
        await WriteAsync(Now, "worked");
        await WriteAsync(Now, "broke", succeeded: false);

        Assert.Equal(["broke"], await SubjectsAsync(new ListAuditEventsQuery(Outcome: AuditOutcome.Failed)));
        Assert.Equal(["worked"], await SubjectsAsync(new ListAuditEventsQuery(Outcome: AuditOutcome.Succeeded)));
    }

    /// <summary>The window is inclusive at both ends and leaves out what lies outside it.</summary>
    [Fact]
    public async Task The_window_is_inclusive_at_both_ends()
    {
        await WriteAsync(Now.AddDays(-1), "before");
        await WriteAsync(Now, "start");
        await WriteAsync(Now.AddDays(1), "end");
        await WriteAsync(Now.AddDays(2), "after");

        var subjects = await SubjectsAsync(new ListAuditEventsQuery(From: Now, To: Now.AddDays(1)));

        Assert.Equal(["end", "start"], subjects);
    }

    /// <summary>A row's action reaches the caller named in the request's language beside the machine code.</summary>
    [Fact]
    public async Task A_rows_action_is_named_in_the_requests_language_beside_the_machine_code()
    {
        await WriteAsync(Now, "livecust", AuditActions.BackupRestored);

        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ru");

            var result = await Handler().HandleAsync(new ListAuditEventsQuery(), CancellationToken.None);

            var row = Assert.Single(result.Value.Items);
            Assert.Equal("BackupRestored", row.Action);
            Assert.Equal("Аккаунт восстановлен из резервной копии", row.ActionName);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>Records one entry.</summary>
    /// <param name="at">When it happened.</param>
    /// <param name="subject">What it was done to; the tests identify rows by it.</param>
    /// <param name="action">What was done.</param>
    /// <param name="succeeded">Whether it worked.</param>
    /// <returns>Resolves once the entry is saved.</returns>
    private async Task WriteAsync(
        DateTimeOffset at,
        string subject,
        string action = AuditActions.LoginSucceeded,
        bool succeeded = true)
    {
        _context.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(), at, null, "admin", action, subject, "203.0.113.7", "agent", succeeded, null));
        await _context.SaveChangesAsync();
    }

    /// <summary>Creates the handler under test over the in-memory journal.</summary>
    /// <returns>The handler.</returns>
    private ListAuditEventsQueryHandler Handler()
    {
        return new ListAuditEventsQueryHandler(_context, IdentityTestContext.ActionNames());
    }

    /// <summary>Reads one page and answers the subjects on it, in order.</summary>
    /// <param name="query">The page to read.</param>
    /// <returns>The subjects of the page's rows.</returns>
    private async Task<IReadOnlyList<string>> SubjectsAsync(ListAuditEventsQuery query)
    {
        var result = await Handler().HandleAsync(query, CancellationToken.None);
        return result.Value.Items
            .Select(auditEvent =>
            {
                return auditEvent.Subject;
            })
            .ToList();
    }
}
