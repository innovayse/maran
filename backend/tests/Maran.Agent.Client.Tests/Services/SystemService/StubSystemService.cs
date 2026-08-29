using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.SystemService;

/// <summary>Stub of <see cref="ISystemServiceInvoker"/> that returns a canned response.</summary>
internal sealed class StubSystemService : ISystemServiceInvoker
{
    /// <summary>The response to return from <see cref="GetAgentInfoAsync"/>.</summary>
    private readonly GetAgentInfoResponse _response;

    /// <summary>Creates a stub that always returns <paramref name="response"/>.</summary>
    /// <param name="response">The canned response to hand back to the caller.</param>
    public StubSystemService(GetAgentInfoResponse response)
    {
        _response = response;
    }

    /// <inheritdoc/>
    public Task<GetAgentInfoResponse> GetAgentInfoAsync(CancellationToken ct)
    {
        return Task.FromResult(_response);
    }
}
