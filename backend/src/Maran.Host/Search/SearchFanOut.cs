using Maran.Host.Search.Enums;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Host.Search;

/// <summary>
/// Asks every permitted search provider the same question and assembles their answers into groups,
/// isolating each provider's failure and time so one slow or broken module costs its own group and
/// nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Providers run one after another, not in parallel, on purpose.</b> They all resolve from the
/// request's scope, and two of them can use the same scoped <c>DbContext</c> (the Sites provider asks the
/// Accounts module's feature window while the Accounts provider is its own caller); a context is not safe
/// for concurrent use. Latency is the sum of the providers, bounded per provider by the budget below.
/// </para>
/// <para>
/// <b>The time budget is enforced on the wait, not only on the provider.</b> The provider receives a
/// token that fires at the budget, and the fan-out also stops waiting then (<c>Task.WaitAsync</c>), so a
/// provider that ignores cancellation is abandoned rather than awaited. An abandoned provider may still
/// be running when the next one starts; that is the price of not letting one module hang the answer, and
/// it can only matter for a provider that ignores cancellation.
/// </para>
/// <para>
/// <b>The term is never logged.</b> A failure is logged with the provider's kind and the exception, and
/// the term is a search box's content: it may be an address or a secret pasted by accident.
/// </para>
/// </remarks>
public sealed class SearchFanOut
{
    /// <summary>
    /// How long one provider may take. Generous for a small indexed read on a warm connection and short
    /// enough that a typist sees the other groups before the next keystroke.
    /// </summary>
    public static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Pre-compiled log delegate for a provider that failed.</summary>
    private static readonly Action<ILogger, SearchKind, Exception?> LogProviderFailed =
        LoggerMessage.Define<SearchKind>(
            LogLevel.Warning,
            new EventId(1, nameof(SearchFanOut)),
            "The {Kind} search provider failed; its group is reported unavailable.");

    /// <summary>Pre-compiled log delegate for a provider that ran out of time.</summary>
    private static readonly Action<ILogger, SearchKind, Exception?> LogProviderTimedOut =
        LoggerMessage.Define<SearchKind>(
            LogLevel.Warning,
            new EventId(2, nameof(SearchFanOut)),
            "The {Kind} search provider exceeded its time budget; its group is reported unavailable.");

    /// <summary>Every provider the composed modules registered.</summary>
    private readonly IReadOnlyList<ISearchProvider> _providers;

    /// <summary>Where a failed or slow provider is recorded.</summary>
    private readonly ILogger<SearchFanOut> _logger;

    /// <summary>Creates the fan-out over the registered providers.</summary>
    /// <param name="providers">Every registered provider.</param>
    /// <param name="logger">Where a failed or slow provider is recorded.</param>
    public SearchFanOut(IEnumerable<ISearchProvider> providers, ILogger<SearchFanOut> logger)
    {
        _providers = providers.ToList();
        _logger = logger;
    }

    /// <summary>Asks every permitted provider and returns one group each.</summary>
    /// <param name="query">The validated term.</param>
    /// <param name="permits">Whether a provider may be asked at all; a refused one has no group.</param>
    /// <param name="providerTimeout">How long one provider may take.</param>
    /// <param name="ct">Cancels the whole search; the caller going away stops the fan-out.</param>
    /// <returns>The groups, ordered by kind, one per provider that was asked.</returns>
    public async Task<IReadOnlyList<SearchGroupDto>> SearchAsync(
        SearchQuery query,
        Func<ISearchProvider, bool> permits,
        TimeSpan providerTimeout,
        CancellationToken ct)
    {
        var groups = new List<SearchGroupDto>();

        foreach (var provider in _providers.Where(permits).OrderBy(candidate => { return candidate.Kind; }))
        {
            groups.Add(await AskAsync(provider, query, providerTimeout, ct));
        }

        return groups;
    }

    /// <summary>Asks one provider, turning a failure or a timeout into an unavailable group.</summary>
    /// <param name="provider">The provider to ask.</param>
    /// <param name="query">The validated term.</param>
    /// <param name="timeout">The provider's time budget.</param>
    /// <param name="ct">Cancels the search as a whole.</param>
    /// <returns>The provider's group.</returns>
    private async Task<SearchGroupDto> AskAsync(
        ISearchProvider provider,
        SearchQuery query,
        TimeSpan timeout,
        CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(timeout);

        try
        {
            var hits = await provider.SearchAsync(query, budget.Token).WaitAsync(timeout, ct);
            return new SearchGroupDto(provider.Kind, SearchGroupState.Ready, hits);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller went away; there is nobody to answer, so let the cancellation travel.
            throw;
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            LogProviderTimedOut(_logger, provider.Kind, null);
            return Unavailable(provider);
        }
        catch (Exception exception)
        {
            // Deliberately broad, with the reason on the line: whatever a module's provider throws, the
            // other modules' groups must still be answered, and the exception text must not reach the
            // caller. It is logged here, server side, with the provider's kind.
            LogProviderFailed(_logger, provider.Kind, exception);
            return Unavailable(provider);
        }
    }

    /// <summary>The group for a provider that could not answer.</summary>
    /// <param name="provider">The provider.</param>
    /// <returns>An unavailable, empty group.</returns>
    private static SearchGroupDto Unavailable(ISearchProvider provider)
    {
        return new SearchGroupDto(provider.Kind, SearchGroupState.Unavailable, []);
    }
}
