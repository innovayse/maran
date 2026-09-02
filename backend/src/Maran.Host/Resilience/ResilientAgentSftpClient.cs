using Maran.Agent.Client.Interfaces;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using Polly;
using Polly.Registry;

namespace Maran.Host.Resilience;

/// <summary>
/// Puts every agent SFTP operation through <see cref="AgentOperationPipeline"/>, for the reason
/// <see cref="ResilientAgentAccountsClient"/> gives: without the decorator the call has no timeout
/// at all, and a stuck unix socket hangs the HTTP request that made it.
/// </summary>
/// <remarks>
/// Deletion is decorated too, and is named here because it is the method this repository has already
/// caught bypassing its pipeline: it ran with no timeout while the whole suite stayed green, since
/// nothing at the call site can see whether the decorator forwarded or the inner client was reached
/// directly.
/// </remarks>
public sealed class ResilientAgentSftpClient : IAgentSftpClient
{
    /// <summary>The client that actually talks to the agent; this type only adds the policy.</summary>
    private readonly IAgentSftpClient _inner;

    /// <summary>The named operation pipeline every call below is executed through.</summary>
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Wraps the real client with the named operation pipeline.</summary>
    /// <param name="inner">The client that actually talks to the agent.</param>
    /// <param name="pipelines">The registry the named pipeline is resolved from.</param>
    public ResilientAgentSftpClient(IAgentSftpClient inner, ResiliencePipelineProvider<string> pipelines)
    {
        _inner = inner;
        _pipeline = pipelines.GetPipeline(AgentOperationPipeline.Name);
    }

    /// <inheritdoc/>
    public async Task<Result<string>> CreateAsync(
        string accountUsername,
        string sftpUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.CreateAsync(
                    state.AccountUsername, state.SftpUsername, state.Password, token);
            },
            (Client: _inner,
             AccountUsername: accountUsername,
             SftpUsername: sftpUsername,
             Password: password),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetPasswordAsync(
        string accountUsername,
        string sftpUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.SetPasswordAsync(
                    state.AccountUsername, state.SftpUsername, state.Password, token);
            },
            (Client: _inner,
             AccountUsername: accountUsername,
             SftpUsername: sftpUsername,
             Password: password),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DeleteAsync(
        string accountUsername,
        string sftpUsername,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.DeleteAsync(state.AccountUsername, state.SftpUsername, token);
            },
            (Client: _inner, AccountUsername: accountUsername, SftpUsername: sftpUsername),
            cancellationToken);
    }
}
