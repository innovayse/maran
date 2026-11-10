using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.Sdk.Modules;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>
/// A module loader that holds a list of loaded modules and can be told to refuse. It applies the check the
/// closed loader applies: a package whose own manifest names another module or version than the signed
/// catalogue announced is refused with <c>ModulePackageNotAsAnnounced</c>, and nothing is loaded.
/// </summary>
public sealed class FakeModuleLoader : IModuleLoader
{
    /// <summary>The modules the loader reports, by module id.</summary>
    private readonly Dictionary<string, LoadedModuleInfo> _loaded = new(StringComparer.Ordinal);

    /// <summary>When set, every change is refused with this error, as the open panel's loader refuses.</summary>
    public Error? Refusal { get; set; }

    /// <summary>The module id the next package's own manifest claims; null means it agrees with what was announced.</summary>
    public string? ClaimedModuleId { get; set; }

    /// <summary>The version the next package's own manifest claims; null means it agrees with what was announced.</summary>
    public string? ClaimedVersion { get; set; }

    /// <summary>How many times an install or upgrade was attempted.</summary>
    public int StageAttempts { get; private set; }

    /// <summary>The ids passed to <see cref="UnloadAsync"/>.</summary>
    public List<string> Unloaded { get; } = [];

    /// <summary>The ids passed to <see cref="RollbackAsync"/>.</summary>
    public List<string> RolledBack { get; } = [];

    /// <summary>Puts a module in the loader's list as if it were already there.</summary>
    /// <param name="moduleId">The module id.</param>
    /// <param name="version">The version.</param>
    /// <param name="state">The lifecycle state.</param>
    /// <param name="failure">Why the module is degraded or failed, or null.</param>
    public void Seed(string moduleId, string version, ModuleLifecycleState state, Error? failure = null)
    {
        _loaded[moduleId] = new LoadedModuleInfo(moduleId, version, state, "marketplace test", failure);
    }

    /// <inheritdoc />
    public Task<Result<ModuleInstallResult>> InstallAsync(IModulePackageSource source, CancellationToken ct)
    {
        return Task.FromResult(Stage(source));
    }

    /// <inheritdoc />
    public Task<Result<ModuleInstallResult>> UpgradeAsync(IModulePackageSource source, CancellationToken ct)
    {
        return Task.FromResult(Stage(source));
    }

    /// <inheritdoc />
    public Task<Result> RollbackAsync(string moduleId, CancellationToken ct)
    {
        RolledBack.Add(moduleId);
        return Task.FromResult(Refusal is null ? Result.Ok() : Result.Fail(Refusal));
    }

    /// <inheritdoc />
    public Task<Result> UnloadAsync(string moduleId, CancellationToken ct)
    {
        Unloaded.Add(moduleId);
        if (Refusal is not null)
        {
            return Task.FromResult(Result.Fail(Refusal));
        }

        if (_loaded.TryGetValue(moduleId, out var current))
        {
            _loaded[moduleId] = current with { State = ModuleLifecycleState.Removed };
        }

        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LoadedModuleInfo>> ListAsync(CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<LoadedModuleInfo>>(_loaded.Values.ToList());
    }

    /// <summary>Verifies the package against what was announced and, when it agrees, records the module as installed.</summary>
    /// <param name="source">The package.</param>
    /// <returns>The install result, or the refusal.</returns>
    private Result<ModuleInstallResult> Stage(IModulePackageSource source)
    {
        StageAttempts++;

        if (Refusal is not null)
        {
            return Result<ModuleInstallResult>.Fail(Refusal);
        }

        if (source is IVerifiedModulePackageSource verified
            && (!string.Equals(ClaimedModuleId ?? verified.Expected.ModuleId, verified.Expected.ModuleId, StringComparison.Ordinal)
                || !string.Equals(ClaimedVersion ?? verified.Expected.Version, verified.Expected.Version, StringComparison.Ordinal)))
        {
            return Result<ModuleInstallResult>.Fail(ModuleRefusals.PackageNotAsAnnounced);
        }

        var expected = ((IVerifiedModulePackageSource)source).Expected;

        // A staged package takes effect at the next start: a module that is already running keeps running
        // its old version, and only a module the loader did not know is listed, as installed.
        if (!_loaded.TryGetValue(expected.ModuleId, out var running) || running.State == ModuleLifecycleState.Removed)
        {
            _loaded[expected.ModuleId] = new LoadedModuleInfo(
                expected.ModuleId, expected.Version, ModuleLifecycleState.Installed, source.Origin, null);
        }

        return Result<ModuleInstallResult>.Ok(
            new ModuleInstallResult(expected.ModuleId, expected.Version, ModuleLifecycleState.Installed));
    }
}
