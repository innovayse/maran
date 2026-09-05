using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Models;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Sites.Tests.Services;

/// <summary>Behavioral contract of <see cref="SiteLogTailService"/>.</summary>
/// <remarks>
/// Two properties are worth more than the rest. A caller may only tail a log for a site their own
/// tenant scope can see, and the answer for one it cannot is "not found" — never "forbidden", which
/// would confirm the site exists. And a stream ALWAYS ends with exactly one frame naming how it
/// ended, whatever the agent did, because a pane that stops updating with nothing said is
/// indistinguishable from a log with nothing more to say.
/// </remarks>
public sealed class SiteLogTailServiceTests
{
    /// <summary>The largest history replay the service accepts — the agent's own cap, from sites.proto.</summary>
    private const int MaxHistoryLines = 1_000;

    /// <summary>Tailing ones own log resolves to the owning accounts system user.</summary>
    [Fact]
    public async Task Tailing_ones_own_log_resolves_to_the_owning_accounts_system_user()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "mine.example.com");
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        var service = Build(context, account);

        var result = await service.ResolveAsync(siteId, "error", 10, "1.2.3.4", "agent", CancellationToken.None);

        Assert.True(result.IsSuccess);

        // The system user comes from the OWNING account, never from anything the caller sent: it is
        // the only thing that decides whose files the agent reads.
        Assert.Equal("owner", result.Value.AccountUsername);
        Assert.Equal("mine.example.com", result.Value.Domain);
        Assert.Equal(SiteLogSource.Error, result.Value.Source);
        Assert.Equal(10u, result.Value.HistoryLines);
    }

    /// <summary>The access log and the error log are told apart and never swapped.</summary>
    [Fact]
    public async Task The_access_log_and_the_error_log_are_told_apart_and_never_swapped()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "mine.example.com");
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        var service = Build(context, account);

        var access = await service.ResolveAsync(siteId, "access", 0, "1.2.3.4", "agent", CancellationToken.None);

        Assert.Equal(SiteLogSource.Access, access.Value.Source);
    }

    /// <summary>Tailing another tenants log answers not found rather than forbidden.</summary>
    [Fact]
    public async Task Tailing_another_tenants_log_answers_not_found_rather_than_forbidden()
    {
        // Definition of Done item 3, at the level where the guarantee actually lives: the tenant
        // query filter, not a check in this service.
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var theirSiteId = await SeedAsync(database, theirs, "theirs.example.com");
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var service = Build(context, theirs);

        var result = await service.ResolveAsync(theirSiteId, "access", 0, "1.2.3.4", "agent", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteNotFound", result.Error!.Code);
        Assert.DoesNotContain("Forbidden", result.Error.Code, StringComparison.Ordinal);
    }

    /// <summary>A malformed request for another tenants log is still answered not found.</summary>
    [Fact]
    public async Task A_malformed_request_for_another_tenants_log_is_still_answered_not_found()
    {
        // The order of the checks is the security property. Validating the parameters first would
        // answer "bad request" for a site that exists and "not found" for one that does not, and
        // the pair is exactly the existence oracle the 404-never-403 rule removes.
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var theirSiteId = await SeedAsync(database, theirs, "theirs.example.com");
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var service = Build(context, theirs);

        var result = await service.ResolveAsync(theirSiteId, "nonsense", -1, "1.2.3.4", "agent", CancellationToken.None);

        Assert.Equal("SiteNotFound", result.Error!.Code);
    }

    /// <summary>A source naming neither of a sites logs is refused rather than guessed.</summary>
    [Fact]
    public async Task A_source_naming_neither_of_a_sites_logs_is_refused_rather_than_guessed()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "mine.example.com");
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        var service = Build(context, account);

        var result = await service.ResolveAsync(siteId, "../../etc/passwd", 0, "1.2.3.4", "agent", CancellationToken.None);

        Assert.Equal("SiteLogSourceInvalid", result.Error!.Code);
    }

    /// <summary>The largest allowed history is accepted and one line more is refused.</summary>
    [Theory]
    [InlineData(0, null)]
    [InlineData(MaxHistoryLines, null)]
    [InlineData(MaxHistoryLines + 1, "SiteLogHistoryLinesInvalid")]
    [InlineData(-1, "SiteLogHistoryLinesInvalid")]
    public async Task The_largest_allowed_history_is_accepted_and_one_line_more_is_refused(
        int historyLines,
        string? expectedCode)
    {
        // The boundary itself, both sides of it. A bound tested only far from its edge survives
        // being moved, and a bound that can be moved without a test noticing is not a bound.
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "mine.example.com");
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        var service = Build(context, account);

        var result = await service.ResolveAsync(
            siteId, "access", historyLines, "1.2.3.4", "agent", CancellationToken.None);

        Assert.Equal(expectedCode, result.IsSuccess ? null : result.Error!.Code);
    }

    /// <summary>An account the directory cannot see answers not found rather than tailing nothing.</summary>
    [Fact]
    public async Task An_account_the_directory_cannot_see_answers_not_found_rather_than_tailing_nothing()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "mine.example.com");
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        var service = Build(context, Guid.NewGuid());

        var result = await service.ResolveAsync(siteId, "access", 0, "1.2.3.4", "agent", CancellationToken.None);

        Assert.Equal("AccountNotFound", result.Error!.Code);
    }

    /// <summary>Every accepted tail is journalled as a success naming the sites domain.</summary>
    [Fact]
    public async Task Every_accepted_tail_is_journalled_as_a_success_naming_the_sites_domain()
    {
        // Definition of Done item 4, on the path that actually happens. Reading a customer's log
        // is the panel opening a window onto their files, and an operator asking later who read
        // what needs the successes as much as the refusals — a journal of refusals alone answers
        // "was anybody turned away", never "who looked".
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "mine.example.com");
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        var audit = new RecordingAuditWriter();
        var service = Build(context, account, audit: audit);

        var result = await service.ResolveAsync(siteId, "error", 0, "1.2.3.4", "agent", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("SiteLogTailed", entry.Action);
        Assert.True(entry.Succeeded);
        Assert.Equal("mine.example.com", entry.Subject);
    }

    /// <summary>Every refused tail is journalled naming what was reached for.</summary>
    [Fact]
    public async Task Every_refused_tail_is_journalled_naming_what_was_reached_for()
    {
        // A probe for a log the caller may not read is the entry an operator later needs; a refusal
        // that returns early past the journal is the half of the journal that goes missing.
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var theirSiteId = await SeedAsync(database, theirs, "theirs.example.com");
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var audit = new RecordingAuditWriter();
        var service = Build(context, theirs, audit: audit);

        await service.ResolveAsync(theirSiteId, "access", 0, "1.2.3.4", "agent", CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("SiteLogTailed", entry.Action);
        Assert.False(entry.Succeeded);
        Assert.Equal(theirSiteId.ToString(), entry.Subject);
    }

    /// <summary>A tail that the agent ends normally is reported as completed.</summary>
    [Fact]
    public async Task A_tail_that_the_agent_ends_normally_is_reported_as_completed()
    {
        var frames = await ReadAsync(
            new SiteLogEvent(SiteLogEventKind.Line, "hello", true, null),
            new SiteLogEvent(SiteLogEventKind.Completed, string.Empty, false, null));

        Assert.Equal(2, frames.Count);
        Assert.Null(frames[0].EndReason);
        Assert.Equal("hello", frames[0].Line);
        Assert.True(frames[0].Historical);
        Assert.Equal(SiteLogEndReason.Completed, frames[^1].EndReason);
    }

    /// <summary>Each interrupted ending keeps its own name and is never softened to completed.</summary>
    [Theory]
    [InlineData(SiteLogEventKind.Dropped, SiteLogEndReason.Dropped)]
    [InlineData(SiteLogEventKind.Idle, SiteLogEndReason.Idle)]
    [InlineData(SiteLogEventKind.Cancelled, SiteLogEndReason.Cancelled)]
    public async Task Each_interrupted_ending_keeps_its_own_name_and_is_never_softened_to_completed(
        SiteLogEventKind kind,
        SiteLogEndReason expected)
    {
        // The reason the agent grew these endings in the first place. Mapped one at a time so a
        // swap between two of them fails naming the pair, rather than passing because the set
        // happens to be the right size.
        var frames = await ReadAsync(new SiteLogEvent(kind, string.Empty, false, null));

        Assert.Equal(expected, Assert.Single(frames).EndReason);
        Assert.NotEqual(SiteLogEndReason.Completed, frames[^1].EndReason);
    }

    /// <summary>A stream the agent ends without naming a reason is reported as truncated.</summary>
    [Fact]
    public async Task A_stream_the_agent_ends_without_naming_a_reason_is_reported_as_truncated()
    {
        var frames = await ReadAsync(new SiteLogEvent(SiteLogEventKind.Line, "orphan", false, null));

        Assert.Equal(SiteLogEndReason.Truncated, frames[^1].EndReason);
    }

    /// <summary>An ending this panel does not recognise is reported as truncated and never as completed.</summary>
    [Fact]
    public async Task An_ending_this_panel_does_not_recognise_is_reported_as_truncated_and_never_as_completed()
    {
        // A kind added to the agent client and forgotten here. The pessimistic answer is the honest
        // one: something may be missing, and we cannot say what.
        var frames = await ReadAsync(new SiteLogEvent((SiteLogEventKind)99, string.Empty, false, null));

        Assert.Equal(SiteLogEndReason.Truncated, Assert.Single(frames).EndReason);
    }

    /// <summary>A failed tail carries a resolved sentence and never the raw error code.</summary>
    [Fact]
    public async Task A_failed_tail_carries_a_resolved_sentence_and_never_the_raw_error_code()
    {
        var errorText = new StubErrorTextProvider();

        var frames = await ReadAsync(
            errorText,
            new SiteLogEvent(SiteLogEventKind.Failed, string.Empty, false, "AgentSystemFailure"));

        Assert.Equal(SiteLogEndReason.Failed, frames[^1].EndReason);
        Assert.Equal("sentence for AgentSystemFailure", frames[^1].EndMessage);
        Assert.Equal(["AgentSystemFailure"], errorText.Resolved);
    }

    /// <summary>A failure the agent left unnamed still resolves to a sentence of this modules own.</summary>
    [Fact]
    public async Task A_failure_the_agent_left_unnamed_still_resolves_to_a_sentence_of_this_modules_own()
    {
        var errorText = new StubErrorTextProvider();

        var frames = await ReadAsync(
            errorText,
            new SiteLogEvent(SiteLogEventKind.Failed, string.Empty, false, null));

        Assert.Equal("sentence for SiteLogTailFailed", frames[^1].EndMessage);
    }

    /// <summary>A caller who stops watching is told the stream was cancelled and not that it completed.</summary>
    [Fact]
    public async Task A_caller_who_stops_watching_is_told_the_stream_was_cancelled_and_not_that_it_completed()
    {
        var agent = new RecordingAgentSitesClient
        {
            LogEvents =
            [
                new SiteLogEvent(SiteLogEventKind.Line, "one", false, null),
                new SiteLogEvent(SiteLogEventKind.Line, "two", false, null),
            ],
        };

        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "mine.example.com");
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        var service = Build(context, account, agent);
        var target = await service.ResolveAsync(siteId, "access", 0, "1.2.3.4", "agent", CancellationToken.None);

        using var stopWatching = new CancellationTokenSource();
        var frames = new List<SiteLogFrame>();
        await foreach (var frame in service.ReadAsync(target.Value, stopWatching.Token))
        {
            frames.Add(frame);
            await stopWatching.CancelAsync();
        }

        Assert.Equal(2, frames.Count);
        Assert.Equal(SiteLogEndReason.Cancelled, frames[^1].EndReason);
    }

    /// <summary>Reads a scripted tail to its end with the default error text double.</summary>
    /// <param name="scripted">The events the agent yields.</param>
    /// <returns>The frames produced.</returns>
    private static Task<List<SiteLogFrame>> ReadAsync(params SiteLogEvent[] scripted)
    {
        return ReadAsync(new StubErrorTextProvider(), scripted);
    }

    /// <summary>Reads a scripted tail to its end.</summary>
    /// <param name="errorText">The error text double to resolve failure codes through.</param>
    /// <param name="scripted">The events the agent yields.</param>
    /// <returns>The frames produced.</returns>
    private static async Task<List<SiteLogFrame>> ReadAsync(
        StubErrorTextProvider errorText,
        params SiteLogEvent[] scripted)
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "mine.example.com");
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        var agent = new RecordingAgentSitesClient { LogEvents = scripted };
        var service = Build(context, account, agent, errorText);
        var target = await service.ResolveAsync(siteId, "access", 0, "1.2.3.4", "agent", CancellationToken.None);

        var frames = new List<SiteLogFrame>();
        await foreach (var frame in service.ReadAsync(target.Value, CancellationToken.None))
        {
            frames.Add(frame);
        }

        return frames;
    }

    /// <summary>Builds the service under test.</summary>
    /// <param name="context">The tenant-scoped context to read through.</param>
    /// <param name="knownAccountId">The account the directory can answer for.</param>
    /// <param name="agent">The agent double, or a silent one.</param>
    /// <param name="errorText">The error text double, or a fresh one.</param>
    /// <param name="audit">The audit double, or a fresh one.</param>
    /// <returns>The service.</returns>
    private static SiteLogTailService Build(
        SitesDbContext context,
        Guid knownAccountId,
        RecordingAgentSitesClient? agent = null,
        StubErrorTextProvider? errorText = null,
        RecordingAuditWriter? audit = null)
    {
        var currentUser = FakeCurrentUser.Customer(knownAccountId);
        return new SiteLogTailService(
            context,
            new StubAccountDirectory(new AccountSnapshot(knownAccountId, "owner", 5, 2, 3, 7, 5, 1_024)),
            agent ?? new RecordingAgentSitesClient(),
            errorText ?? new StubErrorTextProvider(),
            new SiteAuditJournal(audit ?? new RecordingAuditWriter(), currentUser));
    }

    /// <summary>Seeds one site owned by <paramref name="accountId"/>.</summary>
    /// <param name="database">The in-memory database to seed.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domain">The site's primary domain.</param>
    /// <returns>The seeded site's identity.</returns>
    private static async Task<Guid> SeedAsync(string database, Guid accountId, string domain)
    {
        await using var context = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var site = SitesTestContext.PhpSite(accountId, domain);
        context.Sites.Add(site);
        await context.SaveChangesAsync();
        return site.Id;
    }
}
