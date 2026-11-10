using System.Reflection;
using Maran.ArchitectureTests.Fixtures;
using Maran.Host.Modules;
using Maran.Host.OpenApi;
using Maran.Modules.Fleet;
using Maran.Sdk.Interfaces;
using Maran.Sdk.Manifests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Maran.ArchitectureTests;

/// <summary>
/// Reads the composed modules into <see cref="ModuleContractSubject"/> rows, and derives from a
/// SECOND source what that list is supposed to contain.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a list is appended by hand.</b> <see cref="ModuleRegistry.All"/> deliberately omits a module
/// that is not composed yet (Fleet), yet it publishes a manifest. A census over the registry alone
/// would silently skip it, which is the blind-gate pattern of rules/testing.md. The second
/// source, <see cref="ShippedModuleTypes"/>, is every <see cref="IPanelModule"/> compiled into a
/// module assembly the build ships, so a module that is neither in the registry nor appended here
/// is named by <c>ModuleContractTests</c> instead of being skipped. Licensing joined the registry when it
/// got its screen (issue #8) and left this list with it.
/// </para>
/// <para>
/// UNOBSERVED HERE: a module the panel composes by some route other than the registry and the
/// registry. There is none today; the second source would name it if it existed.
/// </para>
/// </remarks>
public static class ModuleContractCensus
{
    /// <summary>
    /// The modules the build ships that <see cref="ModuleRegistry.All"/> does not list, and why each is outside it:
    /// Fleet (issues #180, #319) is the registry of
    /// servers and has no endpoint, menu entry or composition yet, so listing it would put a module in the
    /// catalogue the sidebar has no destination for. The change that composes Fleet removes it from this list.
    /// </summary>
    /// <returns>One instance of each module judged beside the registry's.</returns>
    public static IReadOnlyList<IPanelModule> UnregisteredModules()
    {
        return [new FleetModule()];
    }

    /// <summary>Every module the build ships: the registry's, followed by <see cref="UnregisteredModules"/>.</summary>
    /// <remarks>
    /// The ONE composition of the two lists. A check about "every module" (the agent capability census, the
    /// schema ownership census, the contract census) reads this rather than concatenating them itself, so a
    /// module outside the registry is never covered by one check and skipped by another (issue #472).
    /// </remarks>
    /// <returns>One instance of each module.</returns>
    public static IReadOnlyList<IPanelModule> AllModules()
    {
        return ModuleRegistry.All.Concat(UnregisteredModules()).ToList();
    }

    /// <summary>Every module the panel knows about: the registry plus the modules it does not list.</summary>
    /// <returns>One subject per module, ordered by id.</returns>
    public static IReadOnlyList<ModuleContractSubject> Subjects()
    {
        return AllModules()
            .Select(SubjectOf)
            .OrderBy(subject => { return subject.Manifest.Id; }, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Reads one module into a subject.</summary>
    /// <param name="module">The module to read, real or planted.</param>
    /// <returns>Its manifest, its name and the policies its controllers name.</returns>
    public static ModuleContractSubject SubjectOf(IPanelModule module)
    {
        return SubjectOf(module, ControllerPolicies(module.GetType().Assembly));
    }

    /// <summary>Reads one module into a subject whose controller policies are given and not discovered.</summary>
    /// <remarks>
    /// For a planted module, whose assembly is this test project: reading that assembly would credit the
    /// module with the policies of every fixture controller in it, and an accepting control would be judged
    /// against controllers it never owned.
    /// </remarks>
    /// <param name="module">The module to read.</param>
    /// <param name="controllerPolicies">The policy names its controllers name.</param>
    /// <returns>Its manifest, its name and the given policies.</returns>
    public static ModuleContractSubject SubjectOf(IPanelModule module, IReadOnlySet<string> controllerPolicies)
    {
        return new ModuleContractSubject(module.Manifest, module.Name, controllerPolicies);
    }

    /// <summary>Every concrete <see cref="IPanelModule"/> compiled into a module assembly the build ships.</summary>
    /// <returns>The types, ordered by full name.</returns>
    public static IReadOnlyList<Type> ShippedModuleTypes()
    {
        return ModuleAssemblies.Modules()
            .SelectMany(assembly => { return assembly.GetTypes(); })
            .Where(type => { return typeof(IPanelModule).IsAssignableFrom(type) && type.IsClass && !type.IsAbstract; })
            .OrderBy(type => { return type.FullName; }, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The policy names the Sdk defines, which is all a permission may be.</summary>
    /// <returns>The Sdk's own vocabulary, so the loader's validator and this gate cannot disagree about it.</returns>
    public static IReadOnlySet<string> PolicyVocabulary()
    {
        return ManifestPermissionVocabulary.Policies;
    }

    /// <summary>The panel version this build is, as the OpenAPI document states it.</summary>
    /// <returns>The informational version of the Host assembly without its revision suffix.</returns>
    public static string PanelVersionText()
    {
        return OpenApiBuildVersion.Of(typeof(ModuleRegistry).Assembly);
    }

    /// <summary>Reads the policy names the controllers of one assembly name.</summary>
    /// <param name="assembly">The module assembly.</param>
    /// <returns>Every non-empty <c>Policy</c> of an <c>[Authorize]</c> on a controller class or action.</returns>
    private static HashSet<string> ControllerPolicies(Assembly assembly)
    {
        const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        var policies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var controller in assembly.GetTypes()
                     .Where(type => { return typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract; }))
        {
            var attributes = controller.GetCustomAttributes<AuthorizeAttribute>(true)
                .Concat(controller.GetMethods(Declared).SelectMany(method =>
                {
                    return method.GetCustomAttributes<AuthorizeAttribute>(true);
                }));

            foreach (var attribute in attributes)
            {
                if (!string.IsNullOrEmpty(attribute.Policy))
                {
                    policies.Add(attribute.Policy);
                }
            }
        }

        return policies;
    }
}
