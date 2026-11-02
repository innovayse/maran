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
/// </remarks>
public static class DashboardSectionReader
{
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
    /// <returns>The section, loaded or unavailable.</returns>
    public static async Task<DashboardSection<TResult>> ReadAsync<TResult>(
        IMessageBus bus,
        ILogger logger,
        object query,
        CancellationToken cancellationToken)
    {
        try
        {
            var answer = await bus.InvokeAsync<Result<TResult>>(query, cancellationToken);
            return answer.IsSuccess
                ? DashboardSection.Loaded<TResult>(answer.Value)
                : DashboardSection.Unavailable<TResult>();
        }
        catch (OperationCanceledException)
        {
            // The caller gave up or the request was cancelled; not this reader's to report.
            throw;
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
