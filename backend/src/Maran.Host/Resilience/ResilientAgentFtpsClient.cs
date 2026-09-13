using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.FtpsService;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using Polly;
using Polly.Registry;

namespace Maran.Host.Resilience;

/// <summary>
/// Puts every agent FTPS operation through <see cref="AgentOperationPipeline"/>, for the reason
/// <see cref="ResilientAgentAccountsClient"/> gives: without the decorator the call has no timeout
/// at all, and a stuck unix socket hangs the HTTP request that made it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which of these calls the pipeline may repeat, and why every one of them is safe.</b> The
/// pipeline retries exactly one thing — a <see cref="System.Net.Sockets.SocketException"/>, which is
/// the failure of a call that could not be made — and it does NOT retry a timeout, because the
/// panel's timeout does not stop the agent's work, only its watching of it (see
/// <see cref="AgentOperationPipeline"/>). So the question each method has to answer is not "is this
/// idempotent" but "is a refused connection safe to dial again", and for all seven the answer is
/// the same: a connection the kernel refused delivered no request, so the second attempt is the
/// FIRST attempt at the operation.
/// </para>
/// <para>
/// The distinction earns its keep on exactly the two calls where a second overlapping copy would be
/// visible to a customer. <see cref="EnableAsync"/> and <see cref="ReloadTlsAsync"/> restart a
/// daemon and abort transfers in flight; retried on a timeout they would bounce every live session
/// twice from one operator click. <see cref="CreateUserAsync"/> is the other: a second copy racing
/// the first is the overlapping-call condition the agent's per-account lock exists to refuse, and
/// manufacturing it out of one customer request is the defect the timeout arm was removed for.
/// </para>
/// <para>
/// <b>The limit, stated rather than assumed.</b> A socket error raised part-way through a call that
/// HAD reached the agent is indistinguishable here from a connect failure, so on that path the retry
/// can still overlap. It is left retried deliberately: the agent's own per-account lock is what
/// refuses the overlap, and the panel's job is to surface that refusal rather than hold a second
/// lock of its own. What the panel cannot yet do is surface it WELL — the contract has no busy code,
/// so the refusal arrives as <c>AgentSystemFailure</c>.
/// </para>
/// <para>
/// Every method is decorated, including <see cref="DeleteUserAsync"/>, which is named here because
/// deletion is the method this repository has already caught bypassing its pipeline: it ran with no
/// timeout while the whole suite stayed green, since nothing at the call site can see whether the
/// decorator forwarded or the inner client was reached directly.
/// </para>
/// </remarks>
public sealed class ResilientAgentFtpsClient : IAgentFtpsClient
{
    /// <summary>The client that actually talks to the agent; this type only adds the policy.</summary>
    private readonly IAgentFtpsClient _inner;

    /// <summary>The named operation pipeline every call below is executed through.</summary>
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Wraps the real client with the named operation pipeline.</summary>
    /// <param name="inner">The client that actually talks to the agent.</param>
    /// <param name="pipelines">The registry the named pipeline is resolved from.</param>
    public ResilientAgentFtpsClient(IAgentFtpsClient inner, ResiliencePipelineProvider<string> pipelines)
    {
        _inner = inner;
        _pipeline = pipelines.GetPipeline(AgentOperationPipeline.Name);
    }

    /// <inheritdoc/>
    public async Task<Result<FtpsStatusDto>> EnableAsync(
        string hostname,
        uint passivePortMin,
        uint passivePortMax,
        string passiveAddress,
        uint maxClients,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.EnableAsync(
                    state.Hostname,
                    state.PassivePortMin,
                    state.PassivePortMax,
                    state.PassiveAddress,
                    state.MaxClients,
                    token);
            },
            (Client: _inner,
             Hostname: hostname,
             PassivePortMin: passivePortMin,
             PassivePortMax: passivePortMax,
             PassiveAddress: passiveAddress,
             MaxClients: maxClients),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<FtpsStatusDto>> DisableAsync(CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.DisableAsync(token);
            },
            _inner,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<FtpsStatusDto>> GetStatusAsync(string hostname, CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.GetStatusAsync(state.Hostname, token);
            },
            (Client: _inner, Hostname: hostname),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<FtpsStatusDto>> ReloadTlsAsync(CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.ReloadTlsAsync(token);
            },
            _inner,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<string>> CreateUserAsync(
        CreateFtpsUserArguments arguments,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.CreateUserAsync(state.Arguments, token);
            },
            (Client: _inner, Arguments: arguments),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetPasswordAsync(
        string accountUsername,
        string ftpsUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.SetPasswordAsync(
                    state.AccountUsername, state.FtpsUsername, state.Password, token);
            },
            (Client: _inner,
             AccountUsername: accountUsername,
             FtpsUsername: ftpsUsername,
             Password: password),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DeleteUserAsync(
        string accountUsername,
        string ftpsUsername,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.DeleteUserAsync(state.AccountUsername, state.FtpsUsername, token);
            },
            (Client: _inner, AccountUsername: accountUsername, FtpsUsername: ftpsUsername),
            cancellationToken);
    }
}
