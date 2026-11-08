using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Host.Modules;

/// <summary>
/// The panel's runtime view of its modules: what the start composed, with the state the loader
/// reports for the modules that came from packages. The static <see cref="ModuleRegistry"/> stays the
/// list of shipped modules and is never mutated; this is the type a consumer asks what is installed.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is fixed and what moves.</b> Which modules are composed is decided once, at start
/// (<see cref="PackagedModuleComposition"/>), and does not change while the process runs. Only the
/// STATE of a packaged module moves: the loader may find that a lease lapsed or that a module stopped
/// working, and says so through <see cref="IModuleLoader.ListAsync"/>.
/// </para>
/// <para>
/// <b>Which reported states are believed.</b> <see cref="ModuleLifecycleState.Degraded"/> and
/// <see cref="ModuleLifecycleState.Failed"/> for a composed packaged module are taken from the loader.
/// Every other state it reports for a composed module is not: <see cref="ModuleLifecycleState.Installed"/>
/// and <see cref="ModuleLifecycleState.Removed"/> mean a different version is waiting for the next
/// start, while the version in this process is still running, and a module that is running is
/// <see cref="ModuleLifecycleState.Active"/>. A shipped module is always active and the loader is
/// never asked about it (a module that ships with the panel can be packageable and is still not one the
/// loader holds), so a loader cannot degrade the panel's own modules by reporting an id they share.
/// </para>
/// </remarks>
public sealed class RuntimeModuleRegistry : IModuleRegistry
{
    /// <summary>What this start composed and refused.</summary>
    private readonly PackagedModuleComposition _composition;

    /// <summary>The loader whose reports overlay the state of packaged modules.</summary>
    private readonly IModuleLoader _loader;

    /// <summary>The modules this start composed, in registration order; the catalogue reads their manifests.</summary>
    public IReadOnlyList<IPanelModule> Modules
    {
        get { return _composition.Composed; }
    }

    /// <summary>Creates the registry.</summary>
    /// <param name="composition">What this start composed and refused.</param>
    /// <param name="loader">The loader that reports the state of packaged modules.</param>
    public RuntimeModuleRegistry(PackagedModuleComposition composition, IModuleLoader loader)
    {
        _composition = composition;
        _loader = loader;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RegisteredModule>> ListAsync(CancellationToken cancellationToken)
    {
        var reported = (await _loader.ListAsync(cancellationToken))
            .GroupBy(info => { return info.ModuleId; }, StringComparer.Ordinal)
            .ToDictionary(group => { return group.Key; }, group => { return group.Last(); }, StringComparer.Ordinal);

        var registered = new List<RegisteredModule>();

        foreach (var module in _composition.Composed)
        {
            var manifest = module.Manifest;
            var state = ModuleLifecycleState.Active;

            if (_composition.Packaged.Contains(module)
                && reported.TryGetValue(manifest.Id, out var info)
                && info.State is ModuleLifecycleState.Degraded or ModuleLifecycleState.Failed)
            {
                state = info.State;
            }

            registered.Add(new RegisteredModule(manifest.Id, manifest.Version, manifest.Kind, manifest.Tier, state));
        }

        foreach (var refusal in _composition.Refused)
        {
            var manifest = refusal.Manifest;
            if (manifest is null)
            {
                // A module whose manifest could not be read has no id, version or tier to state; its
                // refusal is journalled by the recorder, and there is no entry of its own to list.
                continue;
            }

            registered.Add(new RegisteredModule(
                manifest.Id, manifest.Version, manifest.Kind, manifest.Tier, ModuleLifecycleState.Failed));
        }

        return registered;
    }
}
