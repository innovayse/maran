using Maran.Modules.Notifications.Common;
using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Domain.Enums;
using Maran.Modules.Notifications.Persistence;
using Maran.Modules.Notifications.Resources;

namespace Maran.Modules.Notifications.Queries.GetAlertDeliveryStatus;

/// <summary>
/// Handles <see cref="GetAlertDeliveryStatusQuery"/> by reading each channel's state and resolving a
/// failure's sentence for the reader.
/// </summary>
/// <remarks>
/// <para>
/// <b>The sentence is resolved here, from the stored code</b>, through the panel's one error-text
/// provider, so it is in the reader's language and a code no table claims reads as the generic
/// sentence rather than as the code (rules/csharp.md "A stored failure is resolved when it is READ").
/// </para>
/// <para>
/// <b>A channel never used is <c>unknown</c>, not <c>ok</c>.</b> Saying "ok" about a path nothing has
/// travelled would be the false green this status exists to prevent.
/// </para>
/// </remarks>
public sealed class GetAlertDeliveryStatusQueryHandler
{
    /// <summary>The state name of a channel whose latest alert arrived.</summary>
    public const string Ok = "ok";

    /// <summary>The state name of a channel whose latest alert failed.</summary>
    public const string Failing = "failing";

    /// <summary>The state name of a channel nothing has been sent on.</summary>
    public const string Unknown = "unknown";

    /// <summary>The module's database context, which owns the channel states.</summary>
    private readonly NotificationsDbContext _dbContext;

    /// <summary>Resolves a stored failure code into a sentence for the reader.</summary>
    private readonly IErrorTextProvider _errorTexts;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="errorTexts">Resolves a stored failure code into a sentence for the reader.</param>
    public GetAlertDeliveryStatusQueryHandler(NotificationsDbContext dbContext, IErrorTextProvider errorTexts)
    {
        _dbContext = dbContext;
        _errorTexts = errorTexts;
    }

    /// <summary>Returns the delivery state of every alert channel.</summary>
    /// <param name="query">The (parameterless) read request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One status per channel.</returns>
    public async Task<Result<AlertDeliveryStatusDto>> HandleAsync(
        GetAlertDeliveryStatusQuery query,
        CancellationToken cancellationToken)
    {
        var states = await _dbContext.AlertChannelStates
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var mail = states.FirstOrDefault(state => { return state.Channel == AlertChannel.Mail; });
        var webhook = states.FirstOrDefault(state => { return state.Channel == AlertChannel.Webhook; });

        return Result<AlertDeliveryStatusDto>.Ok(new AlertDeliveryStatusDto(StatusOf(mail), StatusOf(webhook)));
    }

    /// <summary>Turns one channel's stored state into its status.</summary>
    /// <param name="state">The stored state, or <c>null</c> when nothing was ever sent on the channel.</param>
    /// <returns>The status as the dashboard reads it.</returns>
    private AlertChannelStatusDto StatusOf(AlertChannelState? state)
    {
        if (state is null)
        {
            return new AlertChannelStatusDto(Unknown, null, null);
        }

        if (!state.IsFailing)
        {
            return new AlertChannelStatusDto(Ok, null, null);
        }

        var code = state.LastFailureCode ?? nameof(ErrorMessages.AlertDeliveryFailed);
        var message = _errorTexts.TryResolve(code, out var text)
            ? text
            : _errorTexts.Resolve(nameof(ErrorMessages.AlertDeliveryFailed));

        return new AlertChannelStatusDto(Failing, state.LastFailedAt, message);
    }
}
