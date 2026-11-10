using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Host.Tests.Modules.Fixtures;

/// <summary>A screen-bundle window that reports one description for every module it is asked about and holds no file.</summary>
internal sealed class StubModuleSpaAssets : IModuleSpaAssets
{
    /// <summary>What <see cref="DescribeAsync"/> reports, or null for no bundle.</summary>
    private readonly ModuleSpaDescription? _description;

    /// <summary>Creates the stand-in.</summary>
    /// <param name="description">The description to report for any module, or null.</param>
    public StubModuleSpaAssets(ModuleSpaDescription? description)
    {
        _description = description;
    }

    /// <inheritdoc />
    public Task<ModuleSpaDescription?> DescribeAsync(string moduleId, CancellationToken ct)
    {
        return Task.FromResult(_description);
    }

    /// <inheritdoc />
    public Task<ModuleSpaAsset?> OpenAsync(string moduleId, string version, string relativePath, CancellationToken ct)
    {
        return Task.FromResult<ModuleSpaAsset?>(null);
    }
}
