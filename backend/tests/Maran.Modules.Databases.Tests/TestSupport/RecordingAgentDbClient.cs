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

    /// <summary>Every additional-user creation this client was asked for, in order.</summary>
    public List<AgentUserCreateCall> UserCreates { get; } = [];

    /// <summary>Every additional-user drop this client was asked for, in order.</summary>
    public List<AgentUserDropCall> UserDrops { get; } = [];

    /// <summary>Every privilege change this client was asked for, in order.</summary>
    public List<AgentPrivilegesCall> PrivilegeChanges { get; } = [];

    /// <summary>What the user creation answers; the prefixed name when unset.</summary>
    public Result<string>? CreateUserResult { get; set; }

    /// <summary>What the user drop answers; success when unset.</summary>
    public Result? DropUserResult { get; set; }

    /// <summary>The answers to successive privilege changes, first call first; success once the queue is empty.</summary>
    public Queue<Result> SetPrivilegesAnswers { get; } = new();

    /// <summary>How many times the agent's diagnostic enumerate was called. Must stay zero.</summary>
    public int ListCalls { get; private set; }

    /// <summary>How many times a size was asked for.</summary>
    public int SizeCalls { get; private set; }

    /// <summary>What <see cref="CreateAsync"/> answers; success with the prefixed names by default.</summary>
    public Result<CreatedDatabaseDto>? CreateResult { get; set; }

    /// <summary>What <see cref="DropAsync"/> answers; success by default.</summary>
    public Result? DropResult { get; set; }

    /// <summary>What <see cref="SetPasswordAsync"/> answers; success by default.</summary>
    public Result? SetPasswordResult { get; set; }

    /// <summary>
    /// Every grant-repair pass this client was asked for, in order, each recorded as its
    /// <c>reportOnly</c> flag (true for an inspection, false for a repair). The ORDER is load-bearing: the repair command is required to read a
    /// report-only pass before it acts, so <c>[true, false]</c> and <c>[false]</c> are the difference
    /// between an inspected repair and a blind one.
    /// </summary>
    public List<bool> GrantRepairPasses { get; } = [];

    /// <summary>What a report-only pass answers; an empty census by default.</summary>
    public Result<GrantInspectionReportDto>? ReportResult { get; set; }

    /// <summary>
    /// What an acting pass answers; an empty census by default. Separate from
    /// <see cref="ReportResult"/> so a test can make the two passes DISAGREE, which is the only way to
    /// see whether a handler reported the pass it performed or the pass it planned.
    /// </summary>
    public Result<GrantRepairReportDto>? RepairResult { get; set; }

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
    public Task<Result> DropAsync(
        string accountUsername,
        string databaseName,
        string dbUsername,
        CancellationToken cancellationToken)
    {
        Drops.Add(new AgentDropCall(accountUsername, databaseName, dbUsername));

        return Task.FromResult(DropResult ?? Result.Ok());
    }

    /// <inheritdoc/>
    public Task<Result> SetPasswordAsync(
        string accountUsername,
        string dbUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        PasswordChanges.Add(new AgentSetPasswordCall(accountUsername, dbUsername, password));

        return Task.FromResult(SetPasswordResult ?? Result.Ok());
    }

    /// <inheritdoc/>
    public Task<Result<string>> CreateUserAsync(
        string accountUsername,
        string dbUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        UserCreates.Add(new AgentUserCreateCall(accountUsername, dbUsername, password));

        return Task.FromResult(CreateUserResult ?? Result<string>.Ok($"{accountUsername}_{dbUsername}"));
    }

    /// <inheritdoc/>
    public Task<Result> DropUserAsync(
        string accountUsername,
        string dbUsername,
        CancellationToken cancellationToken)
    {
        UserDrops.Add(new AgentUserDropCall(accountUsername, dbUsername));

        return Task.FromResult(DropUserResult ?? Result.Ok());
    }

    /// <inheritdoc/>
    public Task<Result> SetPrivilegesAsync(
        string accountUsername,
        string dbUsername,
        string databaseName,
        IReadOnlyCollection<DatabasePrivilegeKind> privileges,
        CancellationToken cancellationToken)
    {
        PrivilegeChanges.Add(new AgentPrivilegesCall(accountUsername, dbUsername, databaseName, privileges));

        return Task.FromResult(SetPrivilegesAnswers.Count > 0 ? SetPrivilegesAnswers.Dequeue() : Result.Ok());
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

    /// <inheritdoc/>
    public Task<Result<GrantInspectionReportDto>> InspectGrantsAsync(CancellationToken cancellationToken)
    {
        GrantRepairPasses.Add(true);

        return Task.FromResult(ReportResult ?? Result<GrantInspectionReportDto>.Ok(
            new GrantInspectionReportDto(0, 0, [], [])));
    }

    /// <inheritdoc/>
    public Task<Result<GrantRepairReportDto>> RepairGrantsAsync(CancellationToken cancellationToken)
    {
        GrantRepairPasses.Add(false);

        return Task.FromResult(RepairResult ?? Result<GrantRepairReportDto>.Ok(
            new GrantRepairReportDto(0, 0, [], [])));
    }
}
