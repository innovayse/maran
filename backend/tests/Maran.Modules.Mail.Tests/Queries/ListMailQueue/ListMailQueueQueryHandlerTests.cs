using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Queries.ListMailQueue;
using Maran.Modules.Mail.Resources;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Mail.Tests.Queries.ListMailQueue;

/// <summary>Behaviour of the query that lists the host's mail queue.</summary>
public sealed class ListMailQueueQueryHandlerTests
{
    /// <summary>When the first fixture message arrived; the others follow it by the minute.</summary>
    private static readonly DateTimeOffset Arrived = new(2026, 11, 7, 8, 30, 0, TimeSpan.Zero);

    /// <summary>The agent's listing is carried with its total and its truncation flag.</summary>
    [Fact]
    public async Task The_agents_listing_is_carried_with_its_total_and_its_truncation_flag()
    {
        using var context = new MailTestContext();
        context.Agent.Queue = new AgentMailQueueListing(
            [
                new AgentMailQueueEntry(
                    "4A1B2C3D4E5F",
                    AgentMailQueueName.Deferred,
                    "bounce@example.net",
                    3,
                    2048,
                    Arrived,
                    AgentMailDeferralClass.RemoteRefused),
            ],
            500,
            true);

        var result = await Handler(context).HandleAsync(new ListMailQueueQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(500u, result.Value.QueueTotal);
        Assert.True(result.Value.Truncated);
        var row = Assert.Single(result.Value.Messages.Items);
        Assert.Equal("4A1B2C3D4E5F", row.QueueId);
        Assert.Equal(MailQueueName.Deferred, row.Queue);
        Assert.Equal("bounce@example.net", row.Sender);
        Assert.Equal(3u, row.RecipientCount);
        Assert.Equal(2048ul, row.SizeBytes);
        Assert.Equal(Arrived, row.ArrivedAt);
        Assert.Equal(MailDeferralClass.RemoteRefused, row.DeferralClass);
        Assert.Equal("MailQueueNameDeferred", row.QueueDisplayName);
        Assert.Equal("MailDeferralRemoteRefused", row.DeferralDisplayName);
    }

    /// <summary>The agent is asked for its own default limit.</summary>
    [Fact]
    public async Task The_agent_is_asked_for_its_own_default_limit()
    {
        using var context = new MailTestContext();

        await Handler(context).HandleAsync(new ListMailQueueQuery(), CancellationToken.None);

        Assert.Equal(["ListQueue(limit=0)"], context.Agent.Calls);
    }

    /// <summary>An empty queue is an empty list, not a failure.</summary>
    [Fact]
    public async Task An_empty_queue_is_an_empty_list()
    {
        using var context = new MailTestContext();

        var result = await Handler(context).HandleAsync(new ListMailQueueQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Messages.Items);
        Assert.Equal(0, result.Value.Messages.Total);
        Assert.Equal(0u, result.Value.QueueTotal);
        Assert.False(result.Value.Truncated);
    }

    /// <summary>A failure of the agent is passed through.</summary>
    [Fact]
    public async Task A_failure_of_the_agent_is_passed_through()
    {
        using var context = new MailTestContext();
        context.Agent.FailNext("ListQueue", Error.Of("AgentUnavailable", ErrorType.Failure));

        var result = await Handler(context).HandleAsync(new ListMailQueueQuery(), CancellationToken.None);

        Assert.Equal("AgentUnavailable", result.Error!.Code);
    }

    /// <summary>The page is cut from the listing, newest first, and carries the matching total.</summary>
    [Fact]
    public async Task The_page_is_cut_from_the_listing_newest_first_with_the_matching_total()
    {
        using var context = new MailTestContext();
        context.Agent.Queue = Listing("a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k");

        var result = await Handler(context).HandleAsync(
            new ListMailQueueQuery(Page: 2, PageSize: PageDefaults.AllowedSizes[0]),
            CancellationToken.None);

        // Eleven messages, ten to a page, newest first: the second page holds the oldest one.
        Assert.Equal(11, result.Value.Messages.Total);
        Assert.Equal(["aaaaaaaaaaaa"], Ids(result.Value.Messages.Items));
    }

    /// <summary>A search keeps the messages whose sender or queue id contains the term, and the total follows.</summary>
    [Fact]
    public async Task A_search_keeps_the_messages_whose_sender_or_queue_id_contains_the_term()
    {
        using var context = new MailTestContext();
        context.Agent.Queue = Listing("a", "b", "c");

        var bySender = await Handler(context).HandleAsync(
            new ListMailQueueQuery(Search: SearchTerm.Of("B@EXAMPLE")),
            CancellationToken.None);
        var byQueueId = await Handler(context).HandleAsync(
            new ListMailQueueQuery(Search: SearchTerm.Of("cccc")),
            CancellationToken.None);
        var byNothing = await Handler(context).HandleAsync(
            new ListMailQueueQuery(Search: SearchTerm.Of("nobody")),
            CancellationToken.None);

        Assert.Equal(["bbbbbbbbbbbb"], Ids(bySender.Value.Messages.Items));
        Assert.Equal(["cccccccccccc"], Ids(byQueueId.Value.Messages.Items));
        Assert.Empty(byNothing.Value.Messages.Items);
        Assert.Equal(0, byNothing.Value.Messages.Total);
        Assert.Equal(3u, byNothing.Value.QueueTotal);
    }

    /// <summary>A sort the list names orders the rows, and the opposite direction reverses them.</summary>
    [Fact]
    public async Task A_named_sort_orders_the_rows_and_its_opposite_reverses_them()
    {
        using var context = new MailTestContext();
        context.Agent.Queue = Listing("a", "b", "c");

        var ascending = await Handler(context).HandleAsync(
            new ListMailQueueQuery(Sort: SortRequest.Parse(MailQueueSortFields.Sender)),
            CancellationToken.None);
        var descending = await Handler(context).HandleAsync(
            new ListMailQueueQuery(Sort: SortRequest.Parse("-" + MailQueueSortFields.Sender)),
            CancellationToken.None);

        Assert.Equal(
            ["aaaaaaaaaaaa", "bbbbbbbbbbbb", "cccccccccccc"],
            Ids(ascending.Value.Messages.Items));
        Assert.Equal(
            ["cccccccccccc", "bbbbbbbbbbbb", "aaaaaaaaaaaa"],
            Ids(descending.Value.Messages.Items));
    }

    /// <summary>Reads the queue ids of a page's rows, in the page's order.</summary>
    /// <param name="rows">The rows of a page.</param>
    /// <returns>Their queue ids.</returns>
    private static List<string> Ids(IEnumerable<MailQueueEntryDto> rows)
    {
        return rows
            .Select(row =>
            {
                return row.QueueId;
            })
            .ToList();
    }

    /// <summary>Builds a listing of one message per letter, each a minute newer than the one before.</summary>
    /// <param name="letters">One letter per message; it spells the queue id and the sender.</param>
    /// <returns>The listing, whose total is the number of messages.</returns>
    private static AgentMailQueueListing Listing(params string[] letters)
    {
        var entries = letters
            .Select((letter, index) =>
            {
                return new AgentMailQueueEntry(
                    new string(letter[0], 12),
                    AgentMailQueueName.Deferred,
                    $"{letter}@example.net",
                    1,
                    100,
                    Arrived.AddMinutes(index),
                    AgentMailDeferralClass.None);
            })
            .ToList();

        return new AgentMailQueueListing(entries, (uint)entries.Count, false);
    }

    /// <summary>Builds the handler from the context's parts.</summary>
    /// <param name="context">The test context.</param>
    /// <returns>The handler.</returns>
    private static ListMailQueueQueryHandler Handler(MailTestContext context)
    {
        return new ListMailQueueQueryHandler(context.Agent, new AliasKeyLocalizer<DisplayNames>());
    }
}
