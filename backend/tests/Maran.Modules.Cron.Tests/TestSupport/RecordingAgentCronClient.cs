using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.CronService;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Cron.Tests.TestSupport;

/// <summary>
/// An <see cref="IAgentCronClient"/> double that records every call and answers whatever a test told
/// it to.
/// </summary>
/// <remarks>
/// It stands in for the crontab itself, not merely for a transport. This module keeps no rows, so
/// what other modules' tests seed into an in-memory database — the state an operation acts on — is
/// scripted here instead: <see cref="ListEntriesResult"/> is the account's crontab as far as every
/// handler under test can tell.
///
/// It is deliberately dumb: it replays a script and records what it was asked. It asserts nothing
/// itself; the tests do.
/// </remarks>
public sealed class RecordingAgentCronClient : IAgentCronClient
{
    /// <summary>The identifier this double mints for a creation it was not told how to answer.</summary>
    /// <remarks>
    /// Fixed rather than random so a test can assert the audit subject against it. It is a
    /// well-formed lowercase hyphenated uuid, because that is the only shape the panel's own entry-id
    /// rule accepts and a double answering something else would let a handler pass a value the real
    /// system would refuse.
    /// </remarks>
    public const string MintedEntryId = "3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d";

    /// <summary>Every listing this client was asked for, as the account user name it named.</summary>
    public List<string> Lists { get; } = [];

    /// <summary>Every creation this client was asked for, in order.</summary>
    public List<AgentCreateEntryCall> Creates { get; } = [];

    /// <summary>Every rewrite this client was asked for, in order.</summary>
    public List<AgentUpdateEntryCall> Updates { get; } = [];

    /// <summary>Every removal this client was asked for, in order.</summary>
    public List<AgentEntryCall> Deletes { get; } = [];

    /// <summary>Every enablement change this client was asked for, in order.</summary>
    public List<AgentSetEntryEnabledCall> EnabledChanges { get; } = [];

    /// <summary>Every run-output read this client was asked for, in order.</summary>
    public List<AgentEntryCall> OutputReads { get; } = [];

    /// <summary>Every environment read this client was asked for, as the account user name it named.</summary>
    public List<string> EnvironmentReads { get; } = [];

    /// <summary>Every environment replacement this client was asked for, in order.</summary>
    public List<AgentSetEnvironmentCall> EnvironmentWrites { get; } = [];

    /// <summary>What <see cref="ListEntriesAsync"/> answers; an empty crontab by default.</summary>
    public Result<IReadOnlyList<AgentCronEntry>>? ListEntriesResult { get; set; }

    /// <summary>What <see cref="CreateEntryAsync"/> answers; <see cref="MintedEntryId"/> by default.</summary>
    public Result<string>? CreateEntryResult { get; set; }

    /// <summary>What <see cref="UpdateEntryAsync"/> answers; success by default.</summary>
    public Result<bool>? UpdateEntryResult { get; set; }

    /// <summary>What <see cref="DeleteEntryAsync"/> answers; success by default.</summary>
    public Result<bool>? DeleteEntryResult { get; set; }

    /// <summary>What <see cref="SetEntryEnabledAsync"/> answers; success by default.</summary>
    public Result<bool>? SetEntryEnabledResult { get; set; }

    /// <summary>What <see cref="GetEntryOutputAsync"/> answers; "never run" by default.</summary>
    public Result<AgentCronRunOutput?>? GetEntryOutputResult { get; set; }

    /// <summary>What <see cref="GetEnvironmentAsync"/> answers; no assignments by default.</summary>
    public Result<IReadOnlyList<AgentCronEnvVar>>? GetEnvironmentResult { get; set; }

    /// <summary>What <see cref="SetEnvironmentAsync"/> answers; success by default.</summary>
    public Result<bool>? SetEnvironmentResult { get; set; }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<AgentCronEntry>>> ListEntriesAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        Lists.Add(accountUsername);

        return Task.FromResult(
            ListEntriesResult ?? Result<IReadOnlyList<AgentCronEntry>>.Ok([]));
    }

    /// <inheritdoc/>
    public Task<Result<string>> CreateEntryAsync(
        string accountUsername,
        AgentCronSchedule schedule,
        string command,
        CancellationToken cancellationToken)
    {
        Creates.Add(new AgentCreateEntryCall(accountUsername, schedule, command));

        return Task.FromResult(CreateEntryResult ?? Result<string>.Ok(MintedEntryId));
    }

    /// <inheritdoc/>
    public Task<Result<bool>> UpdateEntryAsync(
        string accountUsername,
        string entryId,
        AgentCronSchedule schedule,
        string command,
        CancellationToken cancellationToken)
    {
        Updates.Add(new AgentUpdateEntryCall(accountUsername, entryId, schedule, command));

        return Task.FromResult(UpdateEntryResult ?? Result<bool>.Ok(true));
    }

    /// <inheritdoc/>
    public Task<Result<bool>> DeleteEntryAsync(
        string accountUsername,
        string entryId,
        CancellationToken cancellationToken)
    {
        Deletes.Add(new AgentEntryCall(accountUsername, entryId));

        return Task.FromResult(DeleteEntryResult ?? Result<bool>.Ok(true));
    }

    /// <inheritdoc/>
    public Task<Result<bool>> SetEntryEnabledAsync(
        string accountUsername,
        string entryId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        EnabledChanges.Add(new AgentSetEntryEnabledCall(accountUsername, entryId, enabled));

        return Task.FromResult(SetEntryEnabledResult ?? Result<bool>.Ok(true));
    }

    /// <inheritdoc/>
    public Task<Result<AgentCronRunOutput?>> GetEntryOutputAsync(
        string accountUsername,
        string entryId,
        CancellationToken cancellationToken)
    {
        OutputReads.Add(new AgentEntryCall(accountUsername, entryId));

        // The default is the "never run" answer, which is a SUCCESS carrying null rather than a
        // failure — an entry that has not fired yet is not an error, and a double that answered one
        // would let a handler mishandling the distinction pass.
        return Task.FromResult(GetEntryOutputResult ?? Result<AgentCronRunOutput?>.Ok(null));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<AgentCronEnvVar>>> GetEnvironmentAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        EnvironmentReads.Add(accountUsername);

        return Task.FromResult(
            GetEnvironmentResult ?? Result<IReadOnlyList<AgentCronEnvVar>>.Ok([]));
    }

    /// <inheritdoc/>
    public Task<Result<bool>> SetEnvironmentAsync(
        string accountUsername,
        IReadOnlyList<AgentCronEnvVar> variables,
        CancellationToken cancellationToken)
    {
        EnvironmentWrites.Add(new AgentSetEnvironmentCall(accountUsername, variables));

        return Task.FromResult(SetEnvironmentResult ?? Result<bool>.Ok(true));
    }
}
