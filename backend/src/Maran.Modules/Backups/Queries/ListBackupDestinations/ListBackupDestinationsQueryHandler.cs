using Maran.Agent.Client.Interfaces;
using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Services;

namespace Maran.Modules.Backups.Queries.ListBackupDestinations;

/// <summary>Reads the recorded backup destinations, default first, and establishes the local path.</summary>
/// <remarks>
/// <para>
/// Unfiltered by tenant on purpose, and safe to be: a destination carries no account id and names no
/// customer's data — it is the operator's own storage configuration, and the surface that reads it is
/// administrators-only, which is why the controller answers 403 rather than the tenant answer of 404.
/// </para>
/// <para>
/// <b>The local destination's directory is ASKED FOR, on this request, and never restated.</b> The
/// agent writes under its own constant and refuses to be told another, so the panel has no value of
/// its own to show; the handshake reports the directory and this is the one place the panel learns
/// it. Read here rather than stored at boot because a stored copy is a second statement — the panel
/// shipped one for a week as <c>Backups__LocalRoot</c>, and on any server whose operator set it, the
/// screen named a directory the archives were not in.
/// </para>
/// <para>
/// <b>An agent that cannot be reached is not an error here.</b> The list of destinations is a fact
/// the panel does hold, and refusing to show it because the path is unknown would hide the schedules
/// and history a destination is the key to. What is withheld is only the path, as <c>null</c>, which
/// the screen states as not established — the alternative, showing the value the panel would have
/// guessed, is the defect this handler exists to close.
/// </para>
/// </remarks>
public sealed class ListBackupDestinationsQueryHandler
{
    /// <summary>The Backups module's database context.</summary>
    private readonly BackupsDbContext _dbContext;

    /// <summary>The agent handshake, which is where the local backup directory is stated.</summary>
    private readonly IAgentSystemClient _agent;

    /// <summary>Names the panel's own seeded destination in the caller's language.</summary>
    private readonly BackupDestinationDisplayNames _destinationNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Backups module's database context.</param>
    /// <param name="agent">The agent's identity client, asked for the directory it backs up into.</param>
    /// <param name="destinationNames">Names the panel's own seeded destination in the caller's language.</param>
    public ListBackupDestinationsQueryHandler(
        BackupsDbContext dbContext,
        IAgentSystemClient agent,
        BackupDestinationDisplayNames destinationNames)
    {
        _dbContext = dbContext;
        _agent = agent;
        _destinationNames = destinationNames;
    }

    /// <summary>Reads every destination.</summary>
    /// <param name="query">The request; it carries no parameters.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The destinations, the default one first and the rest by name.</returns>
    public async Task<IReadOnlyList<BackupDestinationDto>> HandleAsync(
        ListBackupDestinationsQuery query,
        CancellationToken cancellationToken)
    {
        var destinations = await _dbContext.BackupDestinations
            .OrderByDescending(destination => destination.IsDefault)
            .ThenBy(destination => destination.Name)
            .ToListAsync(cancellationToken);

        var hasLocal = destinations.Exists(destination =>
        {
            return destination.Kind == BackupDestinationKind.Local;
        });

        var agentBackupRoot = hasLocal ? await EstablishLocalRootAsync(cancellationToken) : null;

        return destinations
            .Select(destination =>
            {
                return BackupDestinationViewMapper.From(
                    destination,
                    agentBackupRoot,
                    _destinationNames.Of(destination.Id, destination.Name));
            })
            .ToList();
    }

    /// <summary>Asks the agent which directory it writes backups into.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The directory the agent stated, or <c>null</c> when it stated none.</returns>
    /// <remarks>
    /// <para>
    /// A failed handshake, an agent older than the field and an agent that is not there at all are
    /// the same answer to the only question asked here — "does the panel know the path?" — and all
    /// of them are <c>null</c>. They are not merged into a value: there is no directory this method
    /// may return that the agent did not name.
    /// </para>
    /// <para>
    /// <b>The catch is deliberately total, for the reason <c>AgentHealthProbe</c> gives.</b> A dead
    /// unix socket surfaces as a transport exception, and the panel has no handler that turns one
    /// into an answer; letting it out of here would turn the whole destinations screen into a 500,
    /// so an operator whose agent is down could not even see WHICH destinations exist or reach the
    /// history and schedules keyed to them — during, most likely, the incident that stopped the
    /// agent. Only this one question is made total: every operation that acts on a destination still
    /// fails loudly, because "the agent is unreachable" is not an acceptable silence when a backup
    /// was supposed to be taken. A cancelled request is not a failed handshake and is rethrown.
    /// </para>
    /// </remarks>
    private async Task<string?> EstablishLocalRootAsync(CancellationToken cancellationToken)
    {
        try
        {
            var info = await _agent.GetInfoAsync(cancellationToken);

            return info.IsSuccess && !string.IsNullOrEmpty(info.Value.BackupRoot)
                ? info.Value.BackupRoot
                : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Every failure mode is one answer here; see the remarks above.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }
}
