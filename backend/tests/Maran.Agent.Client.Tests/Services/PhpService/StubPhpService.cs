using System.Runtime.CompilerServices;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.PhpService;

/// <summary>Stub of <c>IPhpServiceInvoker</c> returning a canned response and a canned stream.</summary>
internal sealed class StubPhpService : IPhpServiceInvoker
{
    /// <summary>Response returned from <see cref="ListPhpVersionsAsync"/>.</summary>
    public ListPhpVersionsResponse ListResponse { get; set; } = new();

    /// <summary>Response returned from <see cref="ListInstallablePhpVersionsAsync"/>.</summary>
    public ListInstallablePhpVersionsResponse InstallableResponse { get; set; } = new();

    /// <summary>The messages the install stream yields before it ends.</summary>
    public List<InstallPhpVersionResponse> InstallResponses { get; } = [];

    /// <summary>Invoked after each install message is yielded, so a test can cancel mid-stream.</summary>
    public Action? OnInstallYielded { get; set; }

    /// <summary>How many install messages were actually pulled out of the stub.</summary>
    public int InstallYieldedCount { get; private set; }

    /// <summary>The last request <see cref="InstallPhpVersionAsync"/> received.</summary>
    public InstallPhpVersionRequest? LastInstallRequest { get; private set; }

    /// <summary>Response returned from <see cref="ListPhpExtensionsAsync"/>.</summary>
    public ListPhpExtensionsResponse ExtensionsResponse { get; set; } = new();

    /// <summary>The messages the extension-change stream yields before it ends.</summary>
    public List<SetPhpExtensionResponse> ExtensionResponses { get; } = [];

    /// <summary>When set, the extension-change stream throws it before yielding anything, as a dead transport does.</summary>
    public Exception? ExtensionStreamFailure { get; set; }

    /// <summary>How many extension-change messages were actually pulled out of the stub.</summary>
    public int ExtensionYieldedCount { get; private set; }

    /// <summary>The last request <see cref="ListPhpExtensionsAsync"/> received.</summary>
    public ListPhpExtensionsRequest? LastListExtensionsRequest { get; private set; }

    /// <summary>The last request <see cref="SetPhpExtensionAsync"/> received.</summary>
    public SetPhpExtensionRequest? LastSetExtensionRequest { get; private set; }

    /// <summary>Response returned from <see cref="ApplyPhpPoolSettingsAsync"/>.</summary>
    public ApplyPhpPoolSettingsResponse PoolSettingsResponse { get; set; } = new();

    /// <summary>The last request <see cref="ApplyPhpPoolSettingsAsync"/> received.</summary>
    public ApplyPhpPoolSettingsRequest? LastPoolSettingsRequest { get; private set; }

    /// <inheritdoc/>
    public Task<ListPhpVersionsResponse> ListPhpVersionsAsync(
        ListPhpVersionsRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ListResponse);
    }

    /// <inheritdoc/>
    public Task<ListInstallablePhpVersionsResponse> ListInstallablePhpVersionsAsync(
        ListInstallablePhpVersionsRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(InstallableResponse);
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

    /// <inheritdoc/>
    public Task<ListPhpExtensionsResponse> ListPhpExtensionsAsync(
        ListPhpExtensionsRequest request,
        CancellationToken cancellationToken)
    {
        LastListExtensionsRequest = request;

        return Task.FromResult(ExtensionsResponse);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<SetPhpExtensionResponse> SetPhpExtensionAsync(
        SetPhpExtensionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LastSetExtensionRequest = request;

        if (ExtensionStreamFailure is not null)
        {
            throw ExtensionStreamFailure;
        }

        foreach (var response in ExtensionResponses)
        {
            await Task.Yield();
            ExtensionYieldedCount++;
            yield return response;
        }
    }

    /// <inheritdoc/>
    public Task<ApplyPhpPoolSettingsResponse> ApplyPhpPoolSettingsAsync(
        ApplyPhpPoolSettingsRequest request,
        CancellationToken cancellationToken)
    {
        LastPoolSettingsRequest = request;

        return Task.FromResult(PoolSettingsResponse);
    }
}
