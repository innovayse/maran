using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.CronService;

/// <summary>Stub of <c>ICronServiceInvoker</c> returning canned responses and keeping every request.</summary>
/// <remarks>
/// Every captured request here is read by a test. A stub that records a request nothing asserts is
/// worse than one that records nothing: it makes the mapping look covered while every field of it is
/// free to change.
/// </remarks>
internal sealed class StubCronService : ICronServiceInvoker
{
    /// <summary>Response returned from <see cref="ListCronEntriesAsync"/>.</summary>
    public ListCronEntriesResponse ListResponse { get; set; } = new();

    /// <summary>The last listing request the stub received, for asserting the mapping.</summary>
    public ListCronEntriesRequest? LastListRequest { get; private set; }

    /// <summary>Response returned from <see cref="CreateCronEntryAsync"/>.</summary>
    public CreateCronEntryResponse CreateResponse { get; set; } = new();

    /// <summary>The last creation request the stub received, for asserting the mapping.</summary>
    public CreateCronEntryRequest? LastCreateRequest { get; private set; }

    /// <summary>Response returned from <see cref="UpdateCronEntryAsync"/>.</summary>
    public UpdateCronEntryResponse UpdateResponse { get; set; } = new();

    /// <summary>The last update request the stub received, for asserting the mapping.</summary>
    public UpdateCronEntryRequest? LastUpdateRequest { get; private set; }

    /// <summary>Response returned from <see cref="DeleteCronEntryAsync"/>.</summary>
    public DeleteCronEntryResponse DeleteResponse { get; set; } = new();

    /// <summary>The last deletion request the stub received, for asserting the mapping.</summary>
    public DeleteCronEntryRequest? LastDeleteRequest { get; private set; }

    /// <summary>Response returned from <see cref="SetCronEntryEnabledAsync"/>.</summary>
    public SetCronEntryEnabledResponse SetEnabledResponse { get; set; } = new();

    /// <summary>The last enablement request the stub received, for asserting the mapping.</summary>
    public SetCronEntryEnabledRequest? LastSetEnabledRequest { get; private set; }

    /// <summary>Response returned from <see cref="GetCronEntryOutputAsync"/>.</summary>
    public GetCronEntryOutputResponse OutputResponse { get; set; } = new();

    /// <summary>The last output request the stub received, for asserting the mapping.</summary>
    public GetCronEntryOutputRequest? LastOutputRequest { get; private set; }

    /// <summary>Response returned from <see cref="GetCronEnvironmentAsync"/>.</summary>
    public GetCronEnvironmentResponse GetEnvironmentResponse { get; set; } = new();

    /// <summary>The last environment read the stub received, for asserting the mapping.</summary>
    public GetCronEnvironmentRequest? LastGetEnvironmentRequest { get; private set; }

    /// <summary>Response returned from <see cref="SetCronEnvironmentAsync"/>.</summary>
    public SetCronEnvironmentResponse SetEnvironmentResponse { get; set; } = new();

    /// <summary>The last environment write the stub received, for asserting the mapping.</summary>
    public SetCronEnvironmentRequest? LastSetEnvironmentRequest { get; private set; }

    /// <summary>Builds a stub whose listing fails with the agent's own words.</summary>
    /// <param name="code">The failure category the agent reports.</param>
    /// <param name="message">The agent's operator-facing sentence.</param>
    /// <returns>The configured stub.</returns>
    public static StubCronService FailingListWith(ErrorCode code, string message)
    {
        return new StubCronService
        {
            ListResponse = new ListCronEntriesResponse
            {
                Error = new AgentError { Code = code, Message = message },
            },
        };
    }

    /// <inheritdoc/>
    public Task<ListCronEntriesResponse> ListCronEntriesAsync(
        ListCronEntriesRequest request,
        CancellationToken cancellationToken)
    {
        LastListRequest = request;
        return Task.FromResult(ListResponse);
    }

    /// <inheritdoc/>
    public Task<CreateCronEntryResponse> CreateCronEntryAsync(
        CreateCronEntryRequest request,
        CancellationToken cancellationToken)
    {
        LastCreateRequest = request;
        return Task.FromResult(CreateResponse);
    }

    /// <inheritdoc/>
    public Task<UpdateCronEntryResponse> UpdateCronEntryAsync(
        UpdateCronEntryRequest request,
        CancellationToken cancellationToken)
    {
        LastUpdateRequest = request;
        return Task.FromResult(UpdateResponse);
    }

    /// <inheritdoc/>
    public Task<DeleteCronEntryResponse> DeleteCronEntryAsync(
        DeleteCronEntryRequest request,
        CancellationToken cancellationToken)
    {
        LastDeleteRequest = request;
        return Task.FromResult(DeleteResponse);
    }

    /// <inheritdoc/>
    public Task<SetCronEntryEnabledResponse> SetCronEntryEnabledAsync(
        SetCronEntryEnabledRequest request,
        CancellationToken cancellationToken)
    {
        LastSetEnabledRequest = request;
        return Task.FromResult(SetEnabledResponse);
    }

    /// <inheritdoc/>
    public Task<GetCronEntryOutputResponse> GetCronEntryOutputAsync(
        GetCronEntryOutputRequest request,
        CancellationToken cancellationToken)
    {
        LastOutputRequest = request;
        return Task.FromResult(OutputResponse);
    }

    /// <inheritdoc/>
    public Task<GetCronEnvironmentResponse> GetCronEnvironmentAsync(
        GetCronEnvironmentRequest request,
        CancellationToken cancellationToken)
    {
        LastGetEnvironmentRequest = request;
        return Task.FromResult(GetEnvironmentResponse);
    }

    /// <inheritdoc/>
    public Task<SetCronEnvironmentResponse> SetCronEnvironmentAsync(
        SetCronEnvironmentRequest request,
        CancellationToken cancellationToken)
    {
        LastSetEnvironmentRequest = request;
        return Task.FromResult(SetEnvironmentResponse);
    }
}
