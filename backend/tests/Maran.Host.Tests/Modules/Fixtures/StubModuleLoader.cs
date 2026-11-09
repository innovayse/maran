using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.Modules.Fixtures;

/// <summary>A loader that reports a list a test states and refuses to do anything else.</summary>
internal sealed class StubModuleLoader : IModuleLoader
{
    /// <summary>What <see cref="ListAsync"/> reports.</summary>
    private readonly IReadOnlyList<LoadedModuleInfo> _held;

    /// <summary>Creates the stand-in.</summary>
    /// <param name="held">The modules it reports as held.</param>
    public StubModuleLoader(params LoadedModuleInfo[] held)
    {
        _held = held;
    }

    /// <inheritdoc />
    public Task<Result<ModuleInstallResult>> InstallAsync(IModulePackageSource source, CancellationToken ct)
    {
        throw new NotSupportedException("This stand-in only reports a list.");
    }

    /// <inheritdoc />
    public Task<Result<ModuleInstallResult>> UpgradeAsync(IModulePackageSource source, CancellationToken ct)
    {
        throw new NotSupportedException("This stand-in only reports a list.");
    }

    /// <inheritdoc />
    public Task<Result> RollbackAsync(string moduleId, CancellationToken ct)
    {
        throw new NotSupportedException("This stand-in only reports a list.");
    }

    /// <inheritdoc />
    public Task<Result> UnloadAsync(string moduleId, CancellationToken ct)
    {
        throw new NotSupportedException("This stand-in only reports a list.");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LoadedModuleInfo>> ListAsync(CancellationToken ct)
    {
        return Task.FromResult(_held);
    }
}
