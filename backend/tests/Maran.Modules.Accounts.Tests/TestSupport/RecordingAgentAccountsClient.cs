using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AccountsService;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// An <see cref="IAgentAccountsClient"/> double that records what it was asked to do and
/// answers however the test needs.
///
/// The handlers' whole subject is the ORDER of two effects — the agent first, the row second —
/// so a test has to be able to say "the agent refused" and then assert the row did not move.
/// </summary>
public sealed class RecordingAgentAccountsClient : IAgentAccountsClient
{
    /// <summary>The error every call answers with, or null to succeed.</summary>
    private readonly Error? _failure;

    /// <summary>Names the agent was asked to act on, in order, prefixed with the operation.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Creates a client that succeeds at everything.</summary>
    public RecordingAgentAccountsClient()
    {
    }

    /// <summary>Creates a client that refuses every call with <paramref name="failure"/>.</summary>
    /// <param name="failure">The error to answer with.</param>
    public RecordingAgentAccountsClient(Error failure)
    {
        _failure = failure;
    }

    /// <inheritdoc/>
    public Task<Result<CreatedAccountDto>> CreateAsync(
        string username,
        ulong quotaBytes,
        CancellationToken cancellationToken)
    {
        Calls.Add($"create:{username}:{quotaBytes}");
        return Task.FromResult(_failure is null
            ? Result<CreatedAccountDto>.Ok(new CreatedAccountDto($"/home/{username}", 1001))
            : Result<CreatedAccountDto>.Fail(_failure));
    }

    /// <inheritdoc/>
    public Task<Result<bool>> SuspendAsync(string username, CancellationToken cancellationToken)
    {
        Calls.Add($"suspend:{username}");
        return Task.FromResult(Answer());
    }

    /// <inheritdoc/>
    public Task<Result<bool>> UnsuspendAsync(string username, CancellationToken cancellationToken)
    {
        Calls.Add($"unsuspend:{username}");
        return Task.FromResult(Answer());
    }

    /// <inheritdoc/>
    public Task<Result<ulong>> DeleteAsync(string username, CancellationToken cancellationToken)
    {
        Calls.Add($"delete:{username}");
        return Task.FromResult(_failure is null ? Result<ulong>.Ok(4096) : Result<ulong>.Fail(_failure));
    }

    /// <inheritdoc/>
    public Task<Result<bool>> SetQuotaAsync(string username, ulong quotaBytes, CancellationToken cancellationToken)
    {
        Calls.Add($"quota:{username}:{quotaBytes}");
        return Task.FromResult(Answer());
    }

    /// <inheritdoc/>
    public Task<Result<AccountUsageDto>> GetUsageAsync(string username, CancellationToken cancellationToken)
    {
        Calls.Add($"usage:{username}");
        return Task.FromResult(_failure is null
            ? Result<AccountUsageDto>.Ok(new AccountUsageDto(2048, 4096))
            : Result<AccountUsageDto>.Fail(_failure));
    }

    /// <summary>The configured answer for a call returning nothing but success.</summary>
    private Result<bool> Answer()
    {
        return _failure is null ? Result<bool>.Ok(true) : Result<bool>.Fail(_failure);
    }
}
