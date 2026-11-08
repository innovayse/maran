namespace Maran.Sdk.Contracts;

/// <summary>What an install or an upgrade of a module package produced.</summary>
/// <param name="ModuleId">The installed module's <see cref="Manifest.Id"/>.</param>
/// <param name="Version">The installed version, a semantic version.</param>
/// <param name="State">The state the module reached when the call returned.</param>
public sealed record ModuleInstallResult(string ModuleId, string Version, ModuleLifecycleState State);
