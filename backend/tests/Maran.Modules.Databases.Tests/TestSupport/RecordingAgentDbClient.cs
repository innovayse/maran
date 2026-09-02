using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.DbService;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Databases.Tests.TestSupport;

/// <summary>
/// An <see cref="IAgentDbClient"/> double that records every call and answers whatever a test told
/// it to. It counts its ENUMERATE calls as carefully as its mutations, because "the listing never
/// asks the agent" is one of the module's own guarantees and the only way to check it is to watch a
/// double that would have noticed.
/// </summary>
public sealed class RecordingAgentDbClient : IAgentDbClient
{
    /// <summary>Every creation this client was asked for, in order.</summary>
    public List<AgentCreateCall> Creates { get; } = [];

    /// <summary>Every drop this client was asked for, in order.</summary>
    public List<AgentDropCall> Drops { get; } = [];

    /// <summary>Every password change this client was asked for, in order.</summary>
    public List<AgentSetPasswordCall> PasswordChanges { get; } = [];

    /// <summary>How many times the agent's diagnostic enumerate was called. Must stay zero.</summary>
    public int ListCalls { get; private set; }

    /// <summary>How many times a size was asked for.</summary>
    public int SizeCalls { get; private set; }

    /// <summary>What <see cref="CreateAsync"/> answers; success with the prefixed names by default.</summary>
    public Result<CreatedDatabaseDto>? CreateResult { get; set; }

    /// <summary>What <see cref="DropAsync"/> answers; success by default.</summary>
    public Result<bool>? DropResult { get; set; }

    /// <summary>What <see cref="SetPasswordAsync"/> answers; success by default.</summary>
    public Result<bool>? SetPasswordResult { get; set; }

    /// <inheritdoc/>
    public Task<Result<CreatedDatabaseDto>> CreateAsync(
        string accountUsername,
        string databaseName,
        string dbUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        Creates.Add(new AgentCreateCall(accountUsername, databaseName, dbUsername, password));

        // The default answer applies the prefix the real agent applies, so a test that never
        // configures a result still exercises the handler's "record what the agent reported" path
        // rather than a name the handler could have rebuilt for itself.
        return Task.FromResult(CreateResult ?? Result<CreatedDatabaseDto>.Ok(
            new CreatedDatabaseDto($"{accountUsername}_{databaseName}", $"{accountUsername}_{dbUsername}")));
    }

    /// <inheritdoc/>
    public Task<Result<bool>> DropAsync(
        string accountUsername,
        string databaseName,
        string dbUsername,
        CancellationToken cancellationToken)
    {
        Drops.Add(new AgentDropCall(accountUsername, databaseName, dbUsername));

        return Task.FromResult(DropResult ?? Result<bool>.Ok(true));
    }

    /// <inheritdoc/>
    public Task<Result<bool>> SetPasswordAsync(
        string accountUsername,
        string dbUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        PasswordChanges.Add(new AgentSetPasswordCall(accountUsername, dbUsername, password));

        return Task.FromResult(SetPasswordResult ?? Result<bool>.Ok(true));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<DatabaseSummaryDto>>> ListAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        ListCalls++;

        return Task.FromResult(Result<IReadOnlyList<DatabaseSummaryDto>>.Ok([]));
    }

    /// <inheritdoc/>
    public Task<Result<ulong>> GetSizeAsync(
        string accountUsername,
        string databaseName,
        CancellationToken cancellationToken)
    {
        SizeCalls++;

        return Task.FromResult(Result<ulong>.Ok(4096));
    }
}
