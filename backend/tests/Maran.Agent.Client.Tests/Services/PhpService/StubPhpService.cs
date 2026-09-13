using System.Runtime.CompilerServices;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.PhpService;

/// <summary>Stub of <c>IPhpServiceInvoker</c> returning a canned response and a canned stream.</summary>
internal sealed class StubPhpService : IPhpServiceInvoker
{
    /// <summary>Response returned from <see cref="ListPhpVersionsAsync"/>.</summary>
    public ListPhpVersionsResponse ListResponse { get; set; } = new();

    /// <summary>The messages the install stream yields before it ends.</summary>
    public List<InstallPhpVersionResponse> InstallResponses { get; } = [];

    /// <summary>Invoked after each install message is yielded, so a test can cancel mid-stream.</summary>
    public Action? OnInstallYielded { get; set; }

    /// <summary>How many install messages were actually pulled out of the stub.</summary>
    public int InstallYieldedCount { get; private set; }

    /// <summary>The last request <see cref="InstallPhpVersionAsync"/> received.</summary>
    public InstallPhpVersionRequest? LastInstallRequest { get; private set; }

    /// <inheritdoc/>
    public Task<ListPhpVersionsResponse> ListPhpVersionsAsync(
        ListPhpVersionsRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ListResponse);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<InstallPhpVersionResponse> InstallPhpVersionAsync(
        InstallPhpVersionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LastInstallRequest = request;

        foreach (var response in InstallResponses)
        {
            await Task.Yield();
            InstallYieldedCount++;
            yield return response;
            OnInstallYielded?.Invoke();
        }
    }
}
