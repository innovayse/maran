using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Host.IntegrationTests.Modules;

/// <summary>
/// A screen-bundle window for the frame tests: it holds the descriptions and files a test states and
/// serves nothing else. It stands in for the closed loader's window onto a verified package, and like it
/// supplies bytes only - the content types, the allow-list and the headers are the Host's, which is
/// what the tests that use it observe.
/// </summary>
/// <remarks>
/// UNOBSERVED HERE: that a real package's files are read from a verified copy and that a path cannot
/// leave the bundle's folder. This fake looks a path up in a dictionary, so it cannot be fooled by one;
/// the Host's own path rule is what stops a hostile path reaching any window.
/// </remarks>
public sealed class FakeModuleSpaAssets : IModuleSpaAssets
{
    /// <summary>The description each module's bundle reports.</summary>
    private readonly Dictionary<string, ModuleSpaDescription> _descriptions = new(StringComparer.Ordinal);

    /// <summary>The files, by module, version and path.</summary>
    private readonly Dictionary<(string Module, string Version, string Path), byte[]> _files = new();

    /// <summary>Every stream handed out, in order, so a test can ask whether each was closed.</summary>
    public List<DisposalTrackingStream> Opened { get; } = [];

    /// <summary>States what a module's bundle reports.</summary>
    /// <param name="moduleId">The module's id.</param>
    /// <param name="description">The description to report.</param>
    public void Describe(string moduleId, ModuleSpaDescription description)
    {
        _descriptions[moduleId] = description;
    }

    /// <summary>Adds one file to a module's bundle.</summary>
    /// <param name="moduleId">The module's id.</param>
    /// <param name="version">The version the file belongs to.</param>
    /// <param name="path">The file's path under <c>spa/</c>.</param>
    /// <param name="content">The file's bytes.</param>
    public void AddFile(string moduleId, string version, string path, byte[] content)
    {
        _files[(moduleId, version, path)] = content;
    }

    /// <inheritdoc />
    public Task<ModuleSpaDescription?> DescribeAsync(string moduleId, CancellationToken ct)
    {
        return Task.FromResult(_descriptions.GetValueOrDefault(moduleId));
    }

    /// <inheritdoc />
    public Task<ModuleSpaAsset?> OpenAsync(string moduleId, string version, string relativePath, CancellationToken ct)
    {
        if (!_files.TryGetValue((moduleId, version, relativePath), out var content))
        {
            return Task.FromResult<ModuleSpaAsset?>(null);
        }

        var stream = new DisposalTrackingStream(content);
        Opened.Add(stream);

        return Task.FromResult<ModuleSpaAsset?>(new ModuleSpaAsset(stream, content.Length));
    }
}
