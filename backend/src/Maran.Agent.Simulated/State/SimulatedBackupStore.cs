using Maran.Agent.Client.Services.BackupService;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.Validation;

namespace Maran.Agent.Simulated.State;

/// <summary>
/// The backup artifacts of the simulated server, bounded and safe to share between requests. An
/// artifact is a description — its manifest, size and digest — and no archive exists behind it.
/// </summary>
/// <remarks>
/// An entry is stored exactly as the agent lists it, readable with its manifest or unreadable with
/// the reason, so a seed can give a screen a corrupt artifact without a second kind of record.
/// </remarks>
public sealed class SimulatedBackupStore
{
    /// <summary>Serialises every read and write of <see cref="_artifacts"/>.</summary>
    private readonly Lock _gate = new();

    /// <summary>The ceilings this store enforces.</summary>
    private readonly SimulatedAgentLimits _limits;

    /// <summary>The artifacts, keyed by owning account and backup id.</summary>
    private readonly Dictionary<(string Account, string BackupId), AgentBackupSummary> _artifacts = [];

    /// <summary>Creates an empty store bounded by <paramref name="limits"/>.</summary>
    /// <param name="limits">The ceilings this store enforces.</param>
    public SimulatedBackupStore(SimulatedAgentLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        _limits = limits;
    }

    /// <summary>
    /// Returns the artifact stored under an id, or stores the one <paramref name="build"/> describes.
    /// </summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="backupId">Caller-assigned backup identifier.</param>
    /// <param name="build">Describes the artifact when none is stored; not called when one is.</param>
    /// <returns>
    /// The stored or new artifact — a repeated id is answered with what it first produced, which is the
    /// idempotency a backup id promises — or <c>AgentLimitReached</c> when the account or the server
    /// holds as many artifacts as it will, or <c>AgentInvalidInput</c> for a blank or over-long name.
    /// </returns>
    public Result<AgentBackupSummary> GetOrAdd(string account, string backupId, Func<AgentBackupSummary> build)
    {
        if (!SimulatedInput.IsBoundedName(account, _limits.MaxNameLength)
            || !SimulatedInput.IsBoundedName(backupId, _limits.MaxNameLength))
        {
            return Result<AgentBackupSummary>.Fail(SimulatedAgentErrors.InvalidInput());
        }

        lock (_gate)
        {
            if (_artifacts.TryGetValue((account, backupId), out var existing))
            {
                return Result<AgentBackupSummary>.Ok(existing);
            }

            if (_artifacts.Count >= _limits.MaxBackupsTotal
                || _artifacts.Keys.Count(key => { return key.Account == account; }) >= _limits.MaxBackupsPerAccount)
            {
                return Result<AgentBackupSummary>.Fail(SimulatedAgentErrors.LimitReached());
            }

            var created = build();
            _artifacts[(account, backupId)] = created;

            return Result<AgentBackupSummary>.Ok(created);
        }
    }

    /// <summary>Reads one artifact.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="backupId">The backup identifier.</param>
    /// <returns>The artifact, or <c>null</c> when the account holds none by that id.</returns>
    public AgentBackupSummary? Find(string account, string backupId)
    {
        lock (_gate)
        {
            return _artifacts.GetValueOrDefault((account, backupId));
        }
    }

    /// <summary>Lists one account's artifacts, ordered by id so the answer is stable.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <returns>The artifacts; empty for an account with none.</returns>
    public IReadOnlyList<AgentBackupSummary> ListFor(string account)
    {
        lock (_gate)
        {
            return _artifacts
                .Where(artifact => { return artifact.Key.Account == account; })
                .Select(artifact => { return artifact.Value; })
                .OrderBy(summary => { return summary.BackupId; }, StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>Deletes an artifact.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="backupId">The backup identifier.</param>
    /// <returns>Success, or <c>AgentNotFound</c> when the artifact is not there.</returns>
    public Result Delete(string account, string backupId)
    {
        lock (_gate)
        {
            return _artifacts.Remove((account, backupId)) ? Result.Ok() : Result.Fail(SimulatedAgentErrors.NotFound());
        }
    }
}
