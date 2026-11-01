using Maran.Agent.Client.Interfaces;
using Maran.Modules.Monitoring.Common;
using Maran.Modules.Monitoring.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Monitoring.Queries.GetClockSync;

/// <summary>Handles <see cref="GetClockSyncQuery"/> by asking the agent about the host clock (issue #150).</summary>
/// <remarks>
/// Live rather than the latest round's reading, for the reason the metrics read is live: the screen
/// asks what the clock is doing now, and an operator who has just enabled a synchronisation service
/// expects to see it at once. A failed read is the agent's own typed failure, never "not
/// synchronised" — the interface shows that it could not find out.
/// </remarks>
public sealed class GetClockSyncQueryHandler
{
    /// <summary>The resource key prefix of a state's explanation in <c>DisplayNames.resx</c>.</summary>
    private const string KeyPrefix = "ClockSync";

    /// <summary>The agent, which alone can ask the host about its clock.</summary>
    private readonly IAgentMonitorClient _agent;

    /// <summary>The localized explanation of each state.</summary>
    private readonly IStringLocalizer<DisplayNames> _text;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that reads the clock.</param>
    /// <param name="text">The localized explanation of each state.</param>
    public GetClockSyncQueryHandler(IAgentMonitorClient agent, IStringLocalizer<DisplayNames> text)
    {
        _agent = agent;
        _text = text;
    }

    /// <summary>Returns the clock's state with its explanation in the request's culture.</summary>
    /// <param name="query">The (parameterless) read request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The state, or the agent's own typed failure.</returns>
    public async Task<Result<ClockSyncDto>> HandleAsync(GetClockSyncQuery query, CancellationToken cancellationToken)
    {
        var reading = await _agent.GetClockSyncAsync(cancellationToken);

        if (!reading.IsSuccess)
        {
            return Result<ClockSyncDto>.Fail(reading.Error!);
        }

        return Result<ClockSyncDto>.Ok(new ClockSyncDto(reading.Value, _text[KeyPrefix + reading.Value].Value));
    }
}
