using Maran.Sdk.Events;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Tells the other modules that a site was created, once its row is committed (<see cref="SiteCreated"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>After the commit, so it can never undo it.</b> The caller has recorded the site before it asks for the
/// announcement, which is why this type swallows a subscriber's failure instead of returning it: the vhost
/// and the row exist, and answering "failed" for a site that was created would be a lie the customer acts on
/// by creating it again. The failure is logged for the operator.
/// </para>
/// <para>
/// <b>Inline.</b> The bus is invoked in-process, as the account lifecycle's cascades are, so a subscriber
/// has finished (or failed) when this returns and runs as the caller who created the site.
/// </para>
/// <para>
/// <b>No subscriber is not a failure.</b> A panel composed without the Dns module has no handler for the
/// event. The bus is asked who would receive it before anything is invoked, and an event nobody subscribes to
/// is not announced: the bus would refuse it as having no route, which would be logged as a subscriber that
/// failed when there was no subscriber at all.
/// </para>
/// </remarks>
public sealed class SiteCreationAnnouncer
{
    /// <summary>Pre-compiled log delegate for a subscriber that could not finish after a committed creation.</summary>
    private static readonly Action<ILogger, Guid, Exception?> LogSubscriberFailed =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(1, nameof(SiteCreationAnnouncer)),
            "A module could not finish after site {SiteId} was created; the site exists and the module's own screens can repeat the step.");

    /// <summary>The bus the event is invoked on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Where a subscriber's failure is recorded.</summary>
    private readonly ILogger<SiteCreationAnnouncer> _logger;

    /// <summary>Creates the announcer.</summary>
    /// <param name="bus">The bus <see cref="SiteCreated"/> is invoked on.</param>
    /// <param name="logger">Where a subscriber's failure is recorded.</param>
    public SiteCreationAnnouncer(IMessageBus bus, ILogger<SiteCreationAnnouncer> logger)
    {
        _bus = bus;
        _logger = logger;
    }

    /// <summary>Announces the creation. Never throws for a subscriber's failure.</summary>
    /// <param name="created">The site that was created.</param>
    /// <param name="cancellationToken">Cancels the announcement when the request is cancelled.</param>
    /// <returns>A task that completes when every subscriber finished, failed, or the request was cancelled.</returns>
    public async Task AnnounceAsync(SiteCreated created, CancellationToken cancellationToken)
    {
        try
        {
            // Inside the guard with the invocation: asking who would receive the event is the bus's work too,
            // and nothing the bus does may turn a created site into a failed request.
            if (!_bus.PreviewSubscriptions(created).Any())
            {
                return;
            }

            await _bus.InvokeAsync(created, cancellationToken);
        }
        catch (Exception exception)
        {
            LogSubscriberFailed(_logger, created.SiteId, exception);
        }
    }
}
