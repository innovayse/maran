using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.DbService;

/// <summary>Production <see cref="IDbServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcDbServiceInvoker : IDbServiceInvoker
{
    /// <summary>Where the channel for the server the current call is aimed at comes from.</summary>
    private readonly IAgentChannels _channels;

    /// <summary>Wraps <paramref name="channels"/> behind the <see cref="IDbServiceInvoker"/> seam.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    public GrpcDbServiceInvoker(IAgentChannels channels)
    {
        _channels = channels;
    }

    /// <summary>
    /// A generated client over the channel of the server the current call is aimed at, built per use.
    /// Building it is cheap (it holds the channel and nothing else), and building it per call is what
    /// lets one invoker serve every server: the target is read when the call starts, never earlier.
    /// </summary>
    private Maran.Agent.V1.DbService.DbServiceClient Client
    {
        get { return new Maran.Agent.V1.DbService.DbServiceClient(_channels.ForCurrent()); }
    }

    /// <inheritdoc/>
    public async Task<CreateDatabaseResponse> CreateDatabaseAsync(
        CreateDatabaseRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.CreateDatabaseAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DropDatabaseResponse> DropDatabaseAsync(
        DropDatabaseRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.DropDatabaseAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetDatabasePasswordResponse> SetDatabasePasswordAsync(
        SetDatabasePasswordRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.SetDatabasePasswordAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<CreateDatabaseUserResponse> CreateDatabaseUserAsync(
        CreateDatabaseUserRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.CreateDatabaseUserAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DropDatabaseUserResponse> DropDatabaseUserAsync(
        DropDatabaseUserRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.DropDatabaseUserAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetDatabaseUserPrivilegesResponse> SetDatabaseUserPrivilegesAsync(
        SetDatabaseUserPrivilegesRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.SetDatabaseUserPrivilegesAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ListDatabasesResponse> ListDatabasesAsync(
        ListDatabasesRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ListDatabasesAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetDatabaseSizeResponse> GetDatabaseSizeAsync(
        GetDatabaseSizeRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.GetDatabaseSizeAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RepairDatabaseGrantsResponse> RepairDatabaseGrantsAsync(
        RepairDatabaseGrantsRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.RepairDatabaseGrantsAsync(request, cancellationToken: cancellationToken);
    }
}
