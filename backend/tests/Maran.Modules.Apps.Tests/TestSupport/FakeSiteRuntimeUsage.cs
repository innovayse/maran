using Maran.Sdk.Interfaces;

namespace Maran.Modules.Apps.Tests.TestSupport;

/// <summary>An <see cref="ISiteRuntimeUsage"/> double holding the domains each runtime is scripted to be used by.</summary>
public sealed class FakeSiteRuntimeUsage : ISiteRuntimeUsage
{
    /// <summary>The domains served by the web server.</summary>
    public List<string> OnWebServer { get; } = [];

    /// <summary>The domains on each PHP version.</summary>
    public Dictionary<string, List<string>> OnPhp { get; } = [];

    /// <summary>The domains on each Node.js major.</summary>
    public Dictionary<string, List<string>> OnNode { get; } = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListDomainsOnWebServerAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<string>>(OnWebServer);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListDomainsOnPhpAsync(string version, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<string>>(OnPhp.TryGetValue(version, out var domains) ? domains : []);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListDomainsOnNodeAsync(string version, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<string>>(OnNode.TryGetValue(version, out var domains) ? domains : []);
    }
}
