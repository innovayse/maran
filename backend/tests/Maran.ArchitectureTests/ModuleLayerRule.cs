namespace Maran.ArchitectureTests;

/// <summary>
/// One line of the layering law: a layer inside a module and what it must not depend on.
/// </summary>
/// <param name="Layer">The layer's folder, and so its namespace segment under the module root (<c>Domain</c>).</param>
/// <param name="ForbiddenModuleLayers">
/// Layers of the SAME module the layer must not name, as namespace segments under the module root.
/// </param>
/// <param name="ForbiddenNamespaces">Namespaces outside the module the layer must not name, as full prefixes.</param>
public sealed record ModuleLayerRule(
    string Layer,
    IReadOnlyList<string> ForbiddenModuleLayers,
    IReadOnlyList<string> ForbiddenNamespaces);
