using Maran.Sdk.Contracts;

namespace Maran.ArchitectureTests;

/// <summary>
/// Everything <see cref="ModuleContractLaw"/> needs to judge one module, read once so the law itself
/// is a pure function and a planted module can be judged exactly like a real one.
/// </summary>
/// <param name="Manifest">What the module publishes about itself.</param>
/// <param name="ModuleName">The module's <c>IPanelModule.Name</c>, which must equal the manifest's id.</param>
/// <param name="ControllerPolicies">
/// The authorization policy names the module's controllers (class and action level) name.
/// </param>
public sealed record ModuleContractSubject(
    Manifest Manifest,
    string ModuleName,
    IReadOnlySet<string> ControllerPolicies);
