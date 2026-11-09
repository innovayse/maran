namespace Maran.Sdk.Contracts;

/// <summary>One module the loader holds, as <see cref="Interfaces.IModuleLoader.ListAsync"/> reports it.</summary>
/// <param name="ModuleId">The module's <see cref="Manifest.Id"/>.</param>
/// <param name="Version">The loaded version, a semantic version.</param>
/// <param name="State">Where the module stands (<see cref="ModuleLifecycleState"/>).</param>
/// <param name="Origin">Where the package came from, as its <see cref="Interfaces.IModulePackageSource.Origin"/> said.</param>
/// <param name="Failure">
/// Why the module is <see cref="ModuleLifecycleState.Failed"/> or <see cref="ModuleLifecycleState.Degraded"/>; null in every
/// other state.
/// </param>
public sealed record LoadedModuleInfo(string ModuleId, string Version, ModuleLifecycleState State, string Origin, Error? Failure);
