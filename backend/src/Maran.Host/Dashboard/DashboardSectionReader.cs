using Maran.SharedKernel.Results;
using Wolverine;

namespace Maran.Host.Dashboard;

/// <summary>
/// Dispatches one module query on behalf of the landing page's sections and answers with a
/// <see cref="DashboardSection{T}"/>: the value when the module answered, unavailable when it did
/// not.
/// </summary>
/// <remarks>
/// <para>
/// The failure is not swallowed: it becomes the section's state, which the screen draws as "could
/// not be read" — the one honest thing it can say about it — and one module's bad day does not take
/// the whole landing page down. The caller is an administrator by this point, so a refusal is a
/// genuine failure rather than a permission boundary, and the module that refused has already
/// logged why, in its own words, which is where that belongs. A query that threw instead is logged
/// here, since nothing else would.
/// </para>
/// <para>
/// A count is asked of the module as a count — one <c>count(*)</c> — and never read off a page of
/// a listing, which since lists are paged would be the size of one page and not the total.
/// </para>
/// <para>
/// <b>A provider that is slow is unavailable, not awaited.</b> Every read is bounded by
/// <see cref="DefaultSectionTimeout"/> (issue #199): a module whose query hangs — an agent that has
/// stopped answering — marks only its own section, instead of holding the whole screen, or a dashboard
/// stream's next frame, for as long as it likes. The bound ends the WAIT; it does not claim the query
/// stopped, and the caller's own cancellation is still told apart from it and still propagates.
/// </para>
/// </remarks>
public static class DashboardSectionReader
{
    /// <summary>How long one section may take before it is reported unavailable.</summary>
    /// <remarks>
    /// Three seconds: the slowest ordinary section is a database count or one agent round-trip, both
    /// well under a second, so a read still going after three is not coming back in time to be useful on
    /// a screen that refreshes every few seconds.
    /// </remarks>
    public static readonly TimeSpan DefaultSectionTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Pre-compiled log delegate for a section whose query exceeded its time bound.</summary>
    private static readonly Action<ILogger, string, Exception?> LogSlowSection =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2, nameof(DashboardSectionReader)),
            "Dashboard section {Query} did not answer in time");

    /// <summary>Pre-compiled log delegate for a section whose query threw rather than refused.</summary>
    private static readonly Action<ILogger, string, Exception?> LogUnreadableSection =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(DashboardSectionReader)),
            "Dashboard section {Query} could not be read");

    /// <summary>
    /// Dispatches one query and answers with its value as a loaded section, or with an unavailable
    /// one when the module refused or failed.
    /// </summary>
    /// <typeparam name="TResult">What the query answers with.</typeparam>
    /// <param name="bus">The bus to dispatch on.</param>
    /// <param name="logger">Where a query that threw is recorded.</param>
    /// <param name="query">The query to send.</param>
    /// <param name="cancellationToken">Cancels the dispatch.</param>
    /// <param name="timeout">
    /// How long to wait for the module, or <c>null</c> for <see cref="DefaultSectionTimeout"/>.
    /// </param>
    /// <returns>The section, loaded or unavailable.</returns>
    public static async Task<DashboardSection<TResult>> ReadAsync<TResult>(
        IMessageBus bus,
        ILogger logger,
        object query,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(timeout ?? DefaultSectionTimeout);

        try
        {
            var answer = await bus.InvokeAsync<Result<TResult>>(query, bounded.Token);
            return answer.IsSuccess
                ? DashboardSection.Loaded<TResult>(answer.Value)
                : DashboardSection.Unavailable<TResult>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller gave up or the request was cancelled; not this reader's to report.
            throw;
        }
        catch (OperationCanceledException exception)
        {
            // Not the caller's cancellation, so it is the bound above: the module was too slow.
            LogSlowSection(logger, query.GetType().Name, exception);
            return DashboardSection.Unavailable<TResult>();
        }
        catch (Exception exception)
        {
            // Recorded here, because a query that THREW logged nothing of its own; the screen is told
            // through the section's state (see the remarks).
            LogUnreadableSection(logger, query.GetType().Name, exception);
            return DashboardSection.Unavailable<TResult>();
        }
    }
}
