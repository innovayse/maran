using Maran.Sdk.Interfaces;

namespace Maran.Host.Modules;

/// <summary>
/// The set of modules a panel start composes: the modules that ship with it, the modules that arrived
/// as packages and passed every check, and those that arrived and did not. Computed once, before any
/// service is registered, and never changed while the process runs - a package that is installed
/// later joins at the next start (restart-to-activate, rules/architecture.md).
/// </summary>
/// <param name="Composed">
/// Every module the panel registers, in registration order: the shipped ones first, then the packaged
/// ones in the order they were offered. The only list message handlers, validators and controllers are
/// taken from.
/// </param>
/// <param name="Packaged">
/// The composed modules that arrived as packages, a subset of <paramref name="Composed"/> in the same
/// order. Kept apart because a module's <see cref="Maran.Sdk.Contracts.ModuleKind"/> does not say where it
/// came from: several modules that ship with the panel are packageable too, and only the ones that were
/// actually loaded from a package are the loader's to report on.
/// </param>
/// <param name="Refused">The packaged modules that were not composed, each with its reason; empty when none were.</param>
public sealed record PackagedModuleComposition(
    IReadOnlyList<IPanelModule> Composed,
    IReadOnlyList<IPanelModule> Packaged,
    IReadOnlyList<PackagedModuleRefusal> Refused);
