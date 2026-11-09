using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.DbService;
using Maran.Agent.Simulated.State;
using Maran.SharedKernel.Security;

namespace Maran.Agent.Simulated.Services.DbService;

/// <summary>
/// The agent's database operations against the simulated server's memory: nothing is created on a
/// database server, because there is none.
/// </summary>
/// <remarks>
/// A password is accepted and dropped, exactly as the real server keeps only a hash of it and the
/// contract returns none. The grant census answers a table that is always correct, since nothing in
/// memory can hold a wildcard grant this panel did not write.
/// </remarks>
public sealed class SimulatedAgentDbClient : IAgentDbClient
{
    /// <summary>The databases of the simulated server.</summary>
    private readonly SimulatedDatabaseStore _databases;

    /// <summary>Creates a client over the shared simulated state.</summary>
    /// <param name="ledger">The simulated server's state.</param>
    public SimulatedAgentDbClient(SimulatedAgentLedger ledger)
    {
        _databases = ledger.Databases;
    }

    /// <inheritdoc/>
    public Task<Result<CreatedDatabaseDto>> CreateAsync(
        string accountUsername,
        string databaseName,
        string dbUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            _databases.Create(accountUsername, databaseName, dbUsername, SimulatedDatabaseStore.EmptyDatabaseBytes));
    }

    /// <inheritdoc/>
    public Task<Result> DropAsync(
        string accountUsername,
        string databaseName,
        string dbUsername,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_databases.Drop(accountUsername, databaseName, dbUsername));
    }

    /// <inheritdoc/>
    public Task<Result> SetPasswordAsync(
        string accountUsername,
        string dbUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_databases.RequireUser(accountUsername, dbUsername));
    }

    /// <inheritdoc/>
    public Task<Result<string>> CreateUserAsync(
        string accountUsername,
        string dbUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_databases.CreateUser(accountUsername, dbUsername));
    }

    /// <inheritdoc/>
    public Task<Result> DropUserAsync(
        string accountUsername,
        string dbUsername,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_databases.DropUser(accountUsername, dbUsername));
    }

    /// <inheritdoc/>
    public Task<Result> SetPrivilegesAsync(
        string accountUsername,
        string dbUsername,
        string databaseName,
        IReadOnlyCollection<DatabasePrivilegeKind> privileges,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_databases.SetPrivileges(accountUsername, dbUsername, databaseName, privileges));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<DatabaseSummaryDto>>> ListAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The user and the size are left null on purpose: the real agent never establishes them in a
        // listing, and a figure the simulation invented here would be one the real server never gives.
        IReadOnlyList<DatabaseSummaryDto> summaries = _databases
            .ListFor(accountUsername)
            .Select(database => { return new DatabaseSummaryDto(database.FullDatabaseName, null, null); })
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<DatabaseSummaryDto>>.Ok(summaries));
    }

    /// <inheritdoc/>
    public Task<Result<ulong>> GetSizeAsync(
        string accountUsername,
        string databaseName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_databases.GetSize(accountUsername, databaseName));
    }

    /// <inheritdoc/>
    public Task<Result<GrantInspectionReportDto>> InspectGrantsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var count = (uint)_databases.Count;

        return Task.FromResult(Result<GrantInspectionReportDto>.Ok(new GrantInspectionReportDto(count, count, [], [])));
    }

    /// <inheritdoc/>
    public Task<Result<GrantRepairReportDto>> RepairGrantsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var count = (uint)_databases.Count;

        return Task.FromResult(Result<GrantRepairReportDto>.Ok(new GrantRepairReportDto(count, count, [], [])));
    }
}
