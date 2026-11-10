using Maran.Agent.Client.Interfaces;
using Maran.Modules.Apps.Common;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Domain.ValueObjects;
using Maran.Modules.Apps.Mappers;
using Maran.Modules.Apps.Resources;
using Maran.Modules.Apps.Services;

namespace Maran.Modules.Apps.Commands.ControlAppService;

/// <summary>
/// Handles <see cref="ControlAppServiceCommand"/>: asks the agent to start, stop, restart or reload the
/// service of one app and answers what the service manager reports afterwards (issue #517).
/// </summary>
/// <remarks>
/// <para>
/// <b>The host decides whether the app is installed, not the ledger.</b> An app put there from a shell
/// after the panel started has no ledger row yet and is still a service an administrator may restart; the
/// agent answers <c>AgentAppNotInstalled</c> for one that is not there.
/// </para>
/// <para>
/// <b>A Node.js runtime has no service</b> (a site's own unit is the thing that runs), so the panel refuses
/// it with <c>AppsHasNoService</c> before asking.
/// </para>
/// <para>
/// <b>Synchronous, not a task.</b> A service action is seconds, and the answer to it is the status the
/// service reached, which the page shows at once; the journal records every attempt, refusals included.
/// </para>
/// </remarks>
public sealed class ControlAppServiceCommandHandler
{
    /// <summary>The agent's app store surface.</summary>
    private readonly IAgentAppsClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly AppsAuditJournal _journal;

    /// <summary>Names the resulting status in the request's language.</summary>
    private readonly AppDisplayNames _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent's app store surface.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="names">Names the resulting status in the request's language.</param>
    public ControlAppServiceCommandHandler(IAgentAppsClient agent, AppsAuditJournal journal, AppDisplayNames names)
    {
        _agent = agent;
        _journal = journal;
        _names = names;
    }

    /// <summary>Performs the action.</summary>
    /// <param name="command">The validated command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The service status after the action, or the typed failure.</returns>
    public async Task<Result<AppServiceDto>> HandleAsync(
        ControlAppServiceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!AppId.TryParse(command.AppId, out var appId))
        {
            return Result<AppServiceDto>.Fail(Error.Of(nameof(ErrorMessages.AppIdInvalid), ErrorType.Validation));
        }

        if (appId.Family == AppFamily.Node)
        {
            await RecordAsync(command, succeeded: false, cancellationToken);
            return Result<AppServiceDto>.Fail(Error.Of(nameof(ErrorMessages.AppsHasNoService), ErrorType.Validation));
        }

        var outcome = await _agent.ControlServiceAsync(
            appId.Value,
            AgentAppMapper.ToAgentAction(command.Action),
            cancellationToken);

        await RecordAsync(command, outcome.IsSuccess, cancellationToken);

        if (!outcome.IsSuccess)
        {
            return Result<AppServiceDto>.Fail(outcome.Error!);
        }

        var status = AgentAppMapper.ToServiceStatus(outcome.Value);

        return Result<AppServiceDto>.Ok(new AppServiceDto(status, _names.Of(status)));
    }

    /// <summary>Journals one attempt; the subject is the app id and the action, both members of closed sets.</summary>
    /// <param name="command">The command.</param>
    /// <param name="succeeded">Whether the action took effect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    private async Task RecordAsync(ControlAppServiceCommand command, bool succeeded, CancellationToken cancellationToken)
    {
        await _journal.RecordRequestAsync(
            AppsAuditJournal.AppServiceControlled,
            $"{command.AppId};action={command.Action.ToString().ToLowerInvariant()}",
            command.IpAddress,
            command.UserAgent,
            succeeded,
            cancellationToken);
    }
}
