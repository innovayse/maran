using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AccountsService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Stands in for the agent while the panel's own account-deletion cascade is exercised end to end:
/// the real message bus, the real module handlers, the real tenant-scoped contexts and the real
/// PostgreSQL schemas.
/// </summary>
/// <remarks>
/// Only the agent is replaced, and only because it cannot be present: it is a separate root process
/// that creates system users on a provisioned host. What the agent does to the HOST — dropping the
/// databases, revoking the SFTP logins, taking the jail's bind mount down — is settled by the
/// polygon suite against a real machine, which is the only place it can be.
///
/// It is deliberately dumb: it records what it was asked and answers what it was told to. It
/// asserts nothing itself; the tests do.
/// </remarks>
public sealed class StubAgentAccountsClient : IAgentAccountsClient
{
    /// <summary>How many bytes a successful deletion reports having freed.</summary>
    private const ulong BytesFreed = 4096;

    /// <summary>The account names the panel asked the agent to delete, in order.</summary>
    public List<string> Deleted { get; } = [];

    /// <summary>Records the deletion and reports success.</summary>
    /// <param name="username">The account's system user name.</param>
    /// <param name="cancellationToken">Unused: nothing here is slow enough to cancel.</param>
    /// <returns>The bytes a real deletion would have freed.</returns>
    public Task<Result<ulong>> DeleteAsync(string username, CancellationToken cancellationToken)
    {
        Deleted.Add(username);

        return Task.FromResult(Result<ulong>.Ok(BytesFreed));
    }

    /// <summary>Not exercised by these tests; a call would be a test asking the wrong question.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="quotaBytes">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<CreatedAccountDto>> CreateAsync(
        string username,
        ulong quotaBytes,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <summary>Not exercised by these tests.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<bool>> SuspendAsync(string username, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <summary>Not exercised by these tests.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<bool>> UnsuspendAsync(string username, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <summary>Not exercised by these tests.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="quotaBytes">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<bool>> SetQuotaAsync(string username, ulong quotaBytes, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <summary>Not exercised by these tests.</summary>
    /// <param name="username">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<AccountUsageDto>> GetUsageAsync(string username, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
