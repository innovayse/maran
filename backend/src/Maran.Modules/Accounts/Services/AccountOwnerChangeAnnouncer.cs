using Maran.Sdk.Events;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// Tells the other modules that an account has a new owner, once the move is committed
/// (<see cref="AccountOwnerChanged"/>, issue #460).
/// </summary>
/// <remarks>
/// <para>
/// <b>After the commit, so it can never undo it, and so it swallows a subscriber's failure.</b> The
/// former owner's view-as sessions are refused on every request by the ownership check of the bearer
/// handler as soon as the row is written; this announcement only ends their rows. A failure is logged
/// for the operator, and the rows expire with the view-as lifetime whatever the subscriber missed.
/// </para>
/// <para>
/// <b>No subscriber is not a failure.</b> The bus is asked who would receive the event before it is
/// invoked, as <see cref="AccountPlanChangeAnnouncer"/> does.
/// </para>
/// </remarks>
public sealed class AccountOwnerChangeAnnouncer
{
    /// <summary>Pre-compiled log delegate for a subscriber that could not finish after a committed move.</summary>
    private static readonly Action<ILogger, Guid, Exception?> LogSubscriberFailed =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(1, nameof(AccountOwnerChangeAnnouncer)),
            "A module could not finish after account {AccountId} changed owner; the move is committed and the former owner's view-as sessions are refused by the ownership check.");

    /// <summary>The bus the event is invoked on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Where a subscriber's failure is recorded.</summary>
    private readonly ILogger<AccountOwnerChangeAnnouncer> _logger;

    /// <summary>Creates the announcer.</summary>
    /// <param name="bus">The bus <see cref="AccountOwnerChanged"/> is invoked on.</param>
    /// <param name="logger">Where a subscriber's failure is recorded.</param>
    public AccountOwnerChangeAnnouncer(IMessageBus bus, ILogger<AccountOwnerChangeAnnouncer> logger)
    {
        _bus = bus;
        _logger = logger;
    }

    /// <summary>Announces the move. Never throws for a subscriber's failure.</summary>
    /// <param name="accountId">The account that changed owner.</param>
    /// <param name="cancellationToken">Stops the announcement when the request is cancelled.</param>
    /// <returns>A task that completes when the announcement has been handled or has failed.</returns>
    public async Task AnnounceAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var message = new AccountOwnerChanged(accountId);
        if (!_bus.PreviewSubscriptions(message).Any())
        {
            return;
        }

        try
        {
            await _bus.InvokeAsync(message, cancellationToken);
        }
        catch (Exception exception)
        {
            LogSubscriberFailed(_logger, accountId, exception);
        }
    }
}
