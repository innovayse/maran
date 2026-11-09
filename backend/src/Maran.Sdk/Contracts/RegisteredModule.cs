namespace Maran.Sdk.Contracts;

/// <summary>
/// One module in the panel's runtime view: composed with the panel or loaded from a package, with the
/// state it is in. The catalogue is built from these.
/// </summary>
/// <param name="Id">The module's <see cref="Manifest.Id"/>.</param>
/// <param name="Version">The module's version, a semantic version.</param>
/// <param name="Kind">Whether it ships with the panel or came from a package.</param>
/// <param name="Tier">The licence tier it is sold under.</param>
/// <param name="State">Where it stands (<see cref="ModuleLifecycleState"/>); a core module is always active.</param>
public sealed record RegisteredModule(string Id, string Version, ModuleKind Kind, LicenceTier Tier, ModuleLifecycleState State);
