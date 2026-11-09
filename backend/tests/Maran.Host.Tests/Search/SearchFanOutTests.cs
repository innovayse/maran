using Maran.Host.Search;
using Maran.Host.Search.Enums;
using Maran.Host.Tests.Fixtures;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Host.Tests.Search;

/// <summary>
/// Behavioural contract of <see cref="SearchFanOut"/>: one group per permitted provider, and one
/// provider's failure, hang or refusal never costs another provider's group.
/// </summary>
public sealed class SearchFanOutTests
{
    /// <summary>A term that must never appear in a log line.</summary>
    private const string PlantedTerm = "planted-secret-term";

    /// <summary>A short budget so the hang test is fast.</summary>
    private static readonly TimeSpan ShortBudget = TimeSpan.FromMilliseconds(200);

    /// <summary>Control: when every provider completes, every group is ready and carries its hits.</summary>
    [Fact]
    public async Task Every_completing_provider_yields_a_ready_group_with_its_hits()
    {
        var fanOut = Build(out _, new FakeProvider(SearchKind.Site, Hit(SearchKind.Site)), new FakeProvider(SearchKind.Account, Hit(SearchKind.Account)));

        var groups = await fanOut.SearchAsync(new SearchQuery(PlantedTerm), _ => { return true; }, ShortBudget, CancellationToken.None);

        Assert.Equal([SearchKind.Account, SearchKind.Site], groups.Select(group => { return group.Kind; }));
        Assert.All(groups, group => { Assert.Equal(SearchGroupState.Ready, group.State); });
        Assert.All(groups, group => { Assert.Single(group.Hits); });
    }

    /// <summary>A provider that never completes is marked unavailable and the others still answer.</summary>
    [Fact]
    public async Task A_provider_that_never_completes_is_unavailable_and_the_others_still_answer()
    {
        var fanOut = Build(out _, new HangingProvider(SearchKind.Account), new FakeProvider(SearchKind.Site, Hit(SearchKind.Site)));

        var groups = await fanOut.SearchAsync(new SearchQuery(PlantedTerm), _ => { return true; }, ShortBudget, CancellationToken.None);

        Assert.Equal(SearchGroupState.Unavailable, groups.Single(group => { return group.Kind == SearchKind.Account; }).State);
        var site = groups.Single(group => { return group.Kind == SearchKind.Site; });
        Assert.Equal(SearchGroupState.Ready, site.State);
        Assert.Single(site.Hits);
    }

    /// <summary>A provider that throws is unavailable, and neither the term nor the exception text is in the group.</summary>
    [Fact]
    public async Task A_provider_that_throws_is_unavailable_and_the_term_is_never_logged()
    {
        var fanOut = Build(out var log, new ThrowingProvider(SearchKind.Account), new FakeProvider(SearchKind.Site));

        var groups = await fanOut.SearchAsync(new SearchQuery(PlantedTerm), _ => { return true; }, ShortBudget, CancellationToken.None);

        Assert.Equal(SearchGroupState.Unavailable, groups.Single(group => { return group.Kind == SearchKind.Account; }).State);
        // Positive control: the recorder does see the provider's kind, so the absence below means something.
        Assert.Contains(log.Entries, entry => { return entry.Message.Contains("Account", StringComparison.Ordinal); });
        Assert.DoesNotContain(log.Entries, entry => { return entry.Message.Contains(PlantedTerm, StringComparison.Ordinal); });
    }

    /// <summary>A provider the gate refuses is not asked and has no group.</summary>
    [Fact]
    public async Task A_provider_the_gate_refuses_is_not_asked()
    {
        var refused = new FakeProvider(SearchKind.Account, Hit(SearchKind.Account));
        var fanOut = Build(out _, refused, new FakeProvider(SearchKind.Site));

        var groups = await fanOut.SearchAsync(
            new SearchQuery(PlantedTerm),
            provider => { return provider.Kind != SearchKind.Account; },
            ShortBudget,
            CancellationToken.None);

        Assert.Equal(0, refused.Calls);
        Assert.Equal([SearchKind.Site], groups.Select(group => { return group.Kind; }));
    }

    /// <summary>The caller going away cancels the search instead of reporting unavailable groups.</summary>
    [Fact]
    public async Task The_caller_going_away_cancels_the_search()
    {
        var fanOut = Build(out _, new HangingProvider(SearchKind.Account));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await fanOut.SearchAsync(new SearchQuery(PlantedTerm), _ => { return true; }, TimeSpan.FromSeconds(5), cancelled.Token);
        });
    }

    /// <summary>Builds the fan-out over the given providers.</summary>
    /// <param name="log">The recorder the fan-out logs into.</param>
    /// <param name="providers">The providers.</param>
    /// <returns>The fan-out.</returns>
    private static SearchFanOut Build(out LogEntryRecorder<SearchFanOut> log, params ISearchProvider[] providers)
    {
        log = new LogEntryRecorder<SearchFanOut>();
        return new SearchFanOut(providers, log);
    }

    /// <summary>Builds a hit of a kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>A hit.</returns>
    private static SearchHit Hit(SearchKind kind)
    {
        return new SearchHit(kind, Guid.NewGuid(), "title", "subtitle");
    }

    /// <summary>A provider that answers at once.</summary>
    private sealed class FakeProvider : ISearchProvider
    {
        /// <summary>What it answers.</summary>
        private readonly IReadOnlyList<SearchHit> _hits;

        /// <summary>Creates it.</summary>
        /// <param name="kind">Its kind.</param>
        /// <param name="hits">Its answer.</param>
        public FakeProvider(SearchKind kind, params SearchHit[] hits)
        {
            Kind = kind;
            _hits = hits;
        }

        /// <inheritdoc />
        public SearchKind Kind { get; }

        /// <summary>How many times it was asked.</summary>
        public int Calls { get; private set; }

        /// <inheritdoc />
        public Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(_hits);
        }
    }

    /// <summary>A provider that ignores cancellation and never completes.</summary>
    private sealed class HangingProvider : ISearchProvider
    {
        /// <summary>Creates it.</summary>
        /// <param name="kind">Its kind.</param>
        public HangingProvider(SearchKind kind)
        {
            Kind = kind;
        }

        /// <inheritdoc />
        public SearchKind Kind { get; }

        /// <inheritdoc />
        public Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, CancellationToken ct)
        {
            return new TaskCompletionSource<IReadOnlyList<SearchHit>>().Task;
        }
    }

    /// <summary>A provider that throws an exception carrying the term.</summary>
    private sealed class ThrowingProvider : ISearchProvider
    {
        /// <summary>Creates it.</summary>
        /// <param name="kind">Its kind.</param>
        public ThrowingProvider(SearchKind kind)
        {
            Kind = kind;
        }

        /// <inheritdoc />
        public SearchKind Kind { get; }

        /// <inheritdoc />
        public Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, CancellationToken ct)
        {
            throw new InvalidOperationException("boom " + query.Term);
        }
    }
}
