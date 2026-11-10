using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Marketplace;

/// <summary>
/// A module loader for the marketplace tests: it hands every call to the contract tests' fake loader and
/// adds the one thing that fake leaves out, which is what the real loader does on removal - it keeps
/// reporting the module, in the removed state, because its data stays behind.
/// </summary>
/// <remarks>
/// UNOBSERVED HERE: that the real loader reports a removed module this way. The marketplace relies on it
/// (<c>dataKept</c> is the removed state), and the closed loader's own suite is where it is proved; this
/// decorator states the same contract so the marketplace's use of it can be observed over HTTP.
/// </remarks>
public sealed class RemovalReportingModuleLoader : IModuleLoader
{
    /// <summary>Guards <see cref="_removed"/>, which the request thread and the worker both touch.</summary>
    private readonly object _gate = new();

    /// <summary>The loader every call is handed to.</summary>
    private readonly IModuleLoader _inner;

    /// <summary>The modules removed since this loader was created, by id, as they were when they went.</summary>
    private readonly Dictionary<string, LoadedModuleInfo> _removed = new(StringComparer.Ordinal);

    /// <summary>Creates the decorator.</summary>
    /// <param name="inner">The loader every call is handed to.</param>
    public RemovalReportingModuleLoader(IModuleLoader inner)
    {
        _inner = inner;
    }

    /// <inheritdoc />
    public async Task<Result<ModuleInstallResult>> InstallAsync(IModulePackageSource source, CancellationToken ct)
    {
        var installed = await _inner.InstallAsync(source, ct);
        if (installed.IsSuccess)
        {
            lock (_gate)
            {
                _removed.Remove(installed.Value.ModuleId);
            }
        }

        return installed;
    }

    /// <inheritdoc />
    public Task<Result<ModuleInstallResult>> UpgradeAsync(IModulePackageSource source, CancellationToken ct)
    {
        return _inner.UpgradeAsync(source, ct);
    }

    /// <inheritdoc />
    public Task<Result> RollbackAsync(string moduleId, CancellationToken ct)
    {
        return _inner.RollbackAsync(moduleId, ct);
    }

    /// <inheritdoc />
    public async Task<Result> UnloadAsync(string moduleId, CancellationToken ct)
    {
        var before = (await _inner.ListAsync(ct)).FirstOrDefault(module => { return module.ModuleId == moduleId; });
        var unloaded = await _inner.UnloadAsync(moduleId, ct);

        if (unloaded.IsSuccess && before is not null)
        {
            lock (_gate)
            {
                _removed[moduleId] = before with { State = ModuleLifecycleState.Removed, Failure = null };
            }
        }

        return unloaded;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LoadedModuleInfo>> ListAsync(CancellationToken ct)
    {
        var held = await _inner.ListAsync(ct);

        lock (_gate)
        {
            var gone = _removed.Values.Where(removed =>
            {
                return held.All(module => { return module.ModuleId != removed.ModuleId; });
            });

            return held
                .Concat(gone)
                .OrderBy(module => { return module.ModuleId; }, StringComparer.Ordinal)
                .ToList();
        }
    }
}
