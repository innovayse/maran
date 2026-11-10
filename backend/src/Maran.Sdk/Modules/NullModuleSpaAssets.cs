using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Sdk.Modules;

/// <summary>
/// The screen-bundle window the open panel ships: it holds no bundle. It is registered only if nothing
/// else registered an <see cref="IModuleSpaAssets"/> first, so a package loader that provides a real
/// window replaces it without this one being edited.
/// </summary>
public sealed class NullModuleSpaAssets : IModuleSpaAssets
{
    /// <inheritdoc />
    public Task<ModuleSpaDescription?> DescribeAsync(string moduleId, CancellationToken ct)
    {
        return Task.FromResult<ModuleSpaDescription?>(null);
    }

    /// <inheritdoc />
    public Task<ModuleSpaAsset?> OpenAsync(string moduleId, string version, string relativePath, CancellationToken ct)
    {
        return Task.FromResult<ModuleSpaAsset?>(null);
    }
}
