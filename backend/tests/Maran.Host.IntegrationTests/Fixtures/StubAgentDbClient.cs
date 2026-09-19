using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.DbService;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// A database agent double for the grant-repair surface: it answers the repair with a census that
/// carries ANOTHER TENANT'S identifiers, and refuses every other member.
/// </summary>
/// <remarks>
/// <para>
/// The census is deliberately not empty, and the names in it are deliberately not the signed-in
/// customer's. The authorisation question this surface raises is not "may a customer run maintenance"
/// but "who may read a host-wide list of names they do not own", so a double that answered with an
/// empty report would let a refusal test pass while proving nothing about disclosure: the body of a
/// 403 would have had nothing to leak.
/// </para>
/// <para>
/// Every other member throws rather than answering. A double that answered them would be a stub for a
/// surface these tests do not exercise, and a reader could not tell which of its answers were
/// load-bearing.
/// </para>
/// </remarks>
public sealed class StubAgentDbClient : IAgentDbClient
{
    /// <summary>A refused row whose names belong to neither of the seeded tenants.</summary>
    public const string StrangerDatabase = "reporting_metrics";

    /// <summary>The user on that refused row.</summary>
    public const string StrangerUser = "reporting_tool";

    /// <summary>A database a repairable row's old pattern also reached.</summary>
    public const string ExposedDatabase = "otherxtenant";

    /// <inheritdoc/>
    public Task<Result<CreatedDatabaseDto>> CreateAsync(
        string accountUsername,
        string databaseName,
        string dbUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers the grant repair only.");
    }

    /// <inheritdoc/>
    public Task<Result<bool>> DropAsync(
        string accountUsername,
        string databaseName,
        string dbUsername,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers the grant repair only.");
    }

    /// <inheritdoc/>
    public Task<Result<bool>> SetPasswordAsync(
        string accountUsername,
        string dbUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers the grant repair only.");
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<DatabaseSummaryDto>>> ListAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers the grant repair only.");
    }

    /// <inheritdoc/>
    public Task<Result<ulong>> GetSizeAsync(
        string accountUsername,
        string databaseName,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers the grant repair only.");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The same census for either pass, with the one repairable row in whichever bucket the pass
    /// belongs to. One row would be rewritten, so an administrator confirming <c>1</c> is accepted and
    /// one confirming anything else is refused — which is what lets a wire test see the gate.
    /// </remarks>
    public Task<Result<GrantRepairReportDto>> RepairGrantsAsync(
        bool reportOnly,
        CancellationToken cancellationToken)
    {
        var repairable = new RepairedGrantDto("own_shop", "own_shop", [ExposedDatabase]);
        var refused = new RefusedGrantDto("localhost", StrangerDatabase, StrangerUser, "NotThePanelsNaming");

        return Task.FromResult(Result<GrantRepairReportDto>.Ok(new GrantRepairReportDto(
            3,
            1,
            reportOnly ? [] : [repairable],
            reportOnly ? [repairable] : [],
            [refused])));
    }
}
