using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Queries.ListPendingUpdates;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Monitoring.Tests.Queries.ListPendingUpdates;

/// <summary>
/// The pending updates list (issue #324): the agent's whole answer is searched, ordered and paged in
/// memory, and a failed read stays a failure instead of becoming an empty page.
/// </summary>
public sealed class ListPendingUpdatesQueryHandlerTests
{
    /// <summary>With no search and no sort the list answers every update, by package name ascending.</summary>
    [Fact]
    public async Task The_default_list_answers_every_update_by_package_name_ascending()
    {
        var handler = Handler(Update("openssl"), Update("bash"), Update("curl"));

        var result = await handler.HandleAsync(new ListPendingUpdatesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Total);
        Assert.Equal(["bash", "curl", "openssl"], Names(result.Value));
    }

    /// <summary>A row crosses with every field the agent reported, the missing installed version staying null.</summary>
    [Fact]
    public async Task A_row_crosses_with_every_field_and_a_missing_installed_version_stays_null()
    {
        var handler = Handler(
            new AgentPendingUpdate("bash", "5.2-2", "5.2-1", "noble-updates"),
            new AgentPendingUpdate("curl", "8.5-1", null, string.Empty));

        var result = await handler.HandleAsync(new ListPendingUpdatesQuery(), CancellationToken.None);

        Assert.Equal(new PendingUpdateDto("bash", "5.2-2", "5.2-1", "noble-updates"), result.Value.Items[0]);
        Assert.Equal(new PendingUpdateDto("curl", "8.5-1", null, string.Empty), result.Value.Items[1]);
    }

    /// <summary>A search keeps the packages whose name contains the term, ignoring case.</summary>
    [Fact]
    public async Task A_search_keeps_the_packages_whose_name_contains_the_term_ignoring_case()
    {
        var handler = Handler(Update("libssl3"), Update("openssl"), Update("bash"));

        var result = await handler.HandleAsync(
            new ListPendingUpdatesQuery(Search: SearchTerm.Of("SSL")),
            CancellationToken.None);

        Assert.Equal(2, result.Value.Total);
        Assert.Equal(["libssl3", "openssl"], Names(result.Value));
    }

    /// <summary>A search reads the package name only, never the version or the source.</summary>
    [Fact]
    public async Task A_search_reads_the_package_name_only()
    {
        var handler = Handler(
            new AgentPendingUpdate("bash", "5.2-2", "5.2-1", "noble-updates"),
            new AgentPendingUpdate("curl", "8.5-1", "8.4-1", "noble-security"));

        var result = await handler.HandleAsync(
            new ListPendingUpdatesQuery(Search: SearchTerm.Of("security")),
            CancellationToken.None);

        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.Total);
    }

    /// <summary>The percent and underscore characters of a search match themselves and nothing else.</summary>
    /// <param name="term">The term, containing a character a LIKE pattern would treat as a wildcard.</param>
    /// <param name="expected">The only package the term may match.</param>
    [Theory]
    [InlineData("%", "x%y")]
    [InlineData("_", "a_b")]
    public async Task The_wildcard_characters_of_a_search_stay_literal(string term, string expected)
    {
        var handler = Handler(Update("x%y"), Update("a_b"), Update("plain"), Update("zz"));

        var result = await handler.HandleAsync(
            new ListPendingUpdatesQuery(Search: SearchTerm.Of(term)),
            CancellationToken.None);

        Assert.Equal([expected], Names(result.Value));
    }

    /// <summary>A descending package sort reverses the page.</summary>
    [Fact]
    public async Task A_descending_package_sort_reverses_the_page()
    {
        var handler = Handler(Update("bash"), Update("curl"), Update("openssl"));

        var result = await handler.HandleAsync(
            new ListPendingUpdatesQuery(Sort: SortRequest.Parse("-packageName")),
            CancellationToken.None);

        Assert.Equal(["openssl", "curl", "bash"], Names(result.Value));
    }

    /// <summary>The source sort orders by origin and breaks ties by package name.</summary>
    [Fact]
    public async Task The_source_sort_orders_by_origin_and_breaks_ties_by_package_name()
    {
        var handler = Handler(
            new AgentPendingUpdate("curl", "1", null, "updates"),
            new AgentPendingUpdate("bash", "1", null, "security"),
            new AgentPendingUpdate("awk", "1", null, "updates"));

        var result = await handler.HandleAsync(
            new ListPendingUpdatesQuery(Sort: SortRequest.Parse("source")),
            CancellationToken.None);

        Assert.Equal(["bash", "awk", "curl"], Names(result.Value));
    }

    /// <summary>A later page carries only its own rows and the true total.</summary>
    [Fact]
    public async Task A_later_page_carries_only_its_own_rows_and_the_true_total()
    {
        var updates = Enumerable.Range(1, 12)
            .Select(index =>
            {
                return Update($"pkg{index:D2}");
            })
            .ToArray();
        var handler = Handler(updates);

        var result = await handler.HandleAsync(
            new ListPendingUpdatesQuery(Page: 2, PageSize: 10),
            CancellationToken.None);

        Assert.Equal(12, result.Value.Total);
        Assert.Equal(["pkg11", "pkg12"], Names(result.Value));
    }

    /// <summary>A page past the end answers empty with the true total.</summary>
    [Fact]
    public async Task A_page_past_the_end_answers_empty_with_the_true_total()
    {
        var handler = Handler(Update("bash"), Update("curl"));

        var result = await handler.HandleAsync(new ListPendingUpdatesQuery(Page: 5), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(2, result.Value.Total);
    }

    /// <summary>An empty catalogue read successfully is an empty page with a total of zero.</summary>
    [Fact]
    public async Task A_catalogue_read_successfully_with_nothing_pending_is_an_empty_page()
    {
        var handler = Handler();

        var result = await handler.HandleAsync(new ListPendingUpdatesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.Total);
    }

    /// <summary>A failed read is the agent's own failure and is never an empty page.</summary>
    [Fact]
    public async Task A_failed_read_is_the_agents_own_failure_and_never_an_empty_page()
    {
        var agent = new StubAgentMonitorClient
        {
            PendingUpdates = Result<IReadOnlyList<AgentPendingUpdate>>.Fail(
                Error.Of("AgentSystemFailure", ErrorType.Failure)),
        };
        var handler = new ListPendingUpdatesQueryHandler(agent);

        var result = await handler.HandleAsync(new ListPendingUpdatesQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
        Assert.Equal(1, agent.PendingUpdatesCalls);
    }

    /// <summary>Builds the handler over an agent that reports exactly the given updates.</summary>
    /// <param name="updates">What the agent reports.</param>
    /// <returns>The handler.</returns>
    private static ListPendingUpdatesQueryHandler Handler(params AgentPendingUpdate[] updates)
    {
        return new ListPendingUpdatesQueryHandler(new StubAgentMonitorClient
        {
            PendingUpdates = Result<IReadOnlyList<AgentPendingUpdate>>.Ok(updates),
        });
    }

    /// <summary>Builds an update that names only its package.</summary>
    /// <param name="name">The package name.</param>
    /// <returns>The update.</returns>
    private static AgentPendingUpdate Update(string name)
    {
        return new AgentPendingUpdate(name, "2.0", "1.0", "updates");
    }

    /// <summary>The package names on a page, in page order.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The names.</returns>
    private static string[] Names(PagedResult<PendingUpdateDto> page)
    {
        return page.Items
            .Select(item =>
            {
                return item.PackageName;
            })
            .ToArray();
    }
}
