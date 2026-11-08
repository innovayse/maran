using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.CronService;

/// <summary>Production <see cref="ICronServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcCronServiceInvoker : ICronServiceInvoker
{
    /// <summary>Where the channel for the server the current call is aimed at comes from.</summary>
    private readonly IAgentChannels _channels;

    /// <summary>Wraps <paramref name="channels"/> behind the <see cref="ICronServiceInvoker"/> seam.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    public GrpcCronServiceInvoker(IAgentChannels channels)
    {
        _channels = channels;
    }

    /// <summary>
    /// A generated client over the channel of the server the current call is aimed at, built per use.
    /// Building it is cheap (it holds the channel and nothing else), and building it per call is what
    /// lets one invoker serve every server: the target is read when the call starts, never earlier.
    /// </summary>
    private Maran.Agent.V1.CronService.CronServiceClient Client
    {
        get { return new Maran.Agent.V1.CronService.CronServiceClient(_channels.ForCurrent()); }
    }

    /// <inheritdoc/>
    public async Task<ListCronEntriesResponse> ListCronEntriesAsync(
        ListCronEntriesRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ListCronEntriesAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<CreateCronEntryResponse> CreateCronEntryAsync(
        CreateCronEntryRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.CreateCronEntryAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<UpdateCronEntryResponse> UpdateCronEntryAsync(
        UpdateCronEntryRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.UpdateCronEntryAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DeleteCronEntryResponse> DeleteCronEntryAsync(
        DeleteCronEntryRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.DeleteCronEntryAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetCronEntryEnabledResponse> SetCronEntryEnabledAsync(
        SetCronEntryEnabledRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.SetCronEntryEnabledAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetAccountCronSuspendedResponse> SetAccountCronSuspendedAsync(
        SetAccountCronSuspendedRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.SetAccountCronSuspendedAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetCronEntryOutputResponse> GetCronEntryOutputAsync(
        GetCronEntryOutputRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.GetCronEntryOutputAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RunCronEntryResponse> RunCronEntryAsync(
        RunCronEntryRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.RunCronEntryAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetCronEnvironmentResponse> GetCronEnvironmentAsync(
        GetCronEnvironmentRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.GetCronEnvironmentAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetCronEnvironmentResponse> SetCronEnvironmentAsync(
        SetCronEnvironmentRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.SetCronEnvironmentAsync(request, cancellationToken: cancellationToken);
    }
}
