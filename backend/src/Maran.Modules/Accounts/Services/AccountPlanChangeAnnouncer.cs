using Maran.Sdk.Events;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// Tells the other modules that an account's plan figures changed in a way a pool can show, once the
/// change is committed (<see cref="AccountPlanChanged"/>, issue #138).
/// </summary>
/// <remarks>
/// <para>
/// <b>After the commit, so it can never undo it.</b> The caller has saved the plan change before it
/// asks for the announcement, which is why this type swallows a subscriber's failure instead of
/// returning it: nothing a subscriber throws could un-write a row that is already written, and
/// answering the administrator "failed" for a plan that did change would be a lie they act on.
/// The failure is logged for the operator, and the panel's start-up reconciliation converges whatever
/// a subscriber missed — the same recovery <see cref="AccountPlanChanged"/> documents.
/// </para>
/// <para>
/// <b>Inline, one event per account.</b> The bus is invoked in-process, as the account lifecycle's
/// cascades are, so a subscriber has finished (or failed) when this returns. A failure for one account
/// does not stop the next: an edit to a plan with many accounts reaches every account it can. A
/// cancelled request stops the loop, since there is nobody left to wait for it.
/// </para>
/// <para>
/// <b>No subscriber is not a failure.</b> The Sites module (which rewrites the pools) is one of the
/// composed set, and a panel without it has no handler for the event. The bus is asked who would
/// receive it before anything is invoked, and an event nobody subscribes to is not announced: the bus
/// would refuse it as having no route, which would be logged as a subscriber that failed when there
/// was no subscriber at all.
/// </para>
/// </remarks>
public sealed class AccountPlanChangeAnnouncer
{
    /// <summary>Pre-compiled log delegate for a subscriber that could not finish after a committed plan change.</summary>
    private static readonly Action<ILogger, Guid, Exception?> LogSubscriberFailed =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(1, nameof(AccountPlanChangeAnnouncer)),
            "A module could not finish after account {AccountId}'s plan figures changed; the change is committed and the next start converges it.");

    /// <summary>The bus the event is invoked on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Where a subscriber's failure is recorded.</summary>
    private readonly ILogger<AccountPlanChangeAnnouncer> _logger;

    /// <summary>Creates the announcer.</summary>
    /// <param name="bus">The bus <see cref="AccountPlanChanged"/> is invoked on.</param>
    /// <param name="logger">Where a subscriber's failure is recorded.</param>
    public AccountPlanChangeAnnouncer(IMessageBus bus, ILogger<AccountPlanChangeAnnouncer> logger)
    {
        _bus = bus;
        _logger = logger;
    }

    /// <summary>Announces the change for each account. Never throws for a subscriber's failure.</summary>
    /// <param name="accountIds">The accounts whose plan figures changed.</param>
    /// <param name="uploadBoundChanged">Whether the plan's upload bound moved, as <see cref="AccountPlanChanged.UploadBoundChanged"/> says.</param>
    /// <param name="cancellationToken">Stops the announcements when the request is cancelled.</param>
    /// <returns>A task that completes when every account has been announced or the request is cancelled.</returns>
    public async Task AnnounceAsync(
        IReadOnlyCollection<Guid> accountIds,
        bool uploadBoundChanged,
        CancellationToken cancellationToken)
    {
        if (accountIds.Count == 0 || !_bus.PreviewSubscriptions(new AccountPlanChanged(accountIds.First(), uploadBoundChanged)).Any())
        {
            return;
        }

        foreach (var accountId in accountIds)
        {
            try
            {
                await _bus.InvokeAsync(new AccountPlanChanged(accountId, uploadBoundChanged), cancellationToken);
            }
            catch (Exception exception)
            {
                LogSubscriberFailed(_logger, accountId, exception);

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }
}
