using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.AccountsService;

/// <summary>Stub of <c>IAccountsServiceInvoker</c> answering the one rpc these tests read.</summary>
/// <remarks>
/// Only the suspension state is answered; every other member throws, because a stub that shrugged at
/// an unexpected call would let a client reaching for the wrong rpc pass. What is under test here is
/// the mapping of that one answer onto this project's own DTOs, which is where four numbers and two
/// lists sit side by side and a swap compiles.
/// </remarks>
internal sealed class StubAccountsService : IAccountsServiceInvoker
{
    /// <summary>Response returned from <see cref="GetAccountSuspensionStateAsync"/>.</summary>
    public GetAccountSuspensionStateResponse SuspensionStateResponse { get; set; } = new();

    /// <summary>The last suspension-state request the stub received.</summary>
    public GetAccountSuspensionStateRequest? LastSuspensionStateRequest { get; private set; }

    /// <inheritdoc/>
    public Task<GetAccountSuspensionStateResponse> GetAccountSuspensionStateAsync(
        GetAccountSuspensionStateRequest request,
        CancellationToken cancellationToken)
    {
        LastSuspensionStateRequest = request;
        return Task.FromResult(SuspensionStateResponse);
    }

    /// <inheritdoc/>
    public Task<CreateAccountResponse> CreateAccountAsync(
        CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("no test in this file creates an account");
    }

    /// <inheritdoc/>
    public Task<SuspendAccountResponse> SuspendAccountAsync(
        SuspendAccountRequest request,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("no test in this file suspends an account");
    }

    /// <inheritdoc/>
    public Task<UnsuspendAccountResponse> UnsuspendAccountAsync(
        UnsuspendAccountRequest request,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("no test in this file unsuspends an account");
    }

    /// <inheritdoc/>
    public Task<DeleteAccountResponse> DeleteAccountAsync(
        DeleteAccountRequest request,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("no test in this file deletes an account");
    }

    /// <inheritdoc/>
    public Task<SetAccountQuotaResponse> SetAccountQuotaAsync(
        SetAccountQuotaRequest request,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("no test in this file sets a quota");
    }

    /// <inheritdoc/>
    public Task<GetAccountUsageResponse> GetAccountUsageAsync(
        GetAccountUsageRequest request,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("no test in this file reads usage");
    }
}
